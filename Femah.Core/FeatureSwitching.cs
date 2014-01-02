﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using System.Web;
using System.Collections.Specialized;

namespace Femah.Core
{
    public sealed class FeatureSwitching
    {
        private static bool _initialised = false;
        private static IFeatureSwitchProvider _provider = null;
        private static Dictionary<int,string> _switches = null;
        private static List<Type> _switchTypes = null;
        private static IFemahContextFactory _contextFactory { get; set; }

        #region Public Methods

        /// <summary>
        /// Initialise the feature switching engine.
        /// </summary>
        /// <param name="provider">The feature switch provider to use to persist feature switches.</param>
        public static void Initialise(FemahConfiguration config = null)
        {
            if (config == null)
            {
                config = new FemahConfiguration();
            }

            // Save context factory.
            _contextFactory = config.ContextFactory ?? new FemahContextFactory();

            // Save provider.
            _provider = config.Provider ?? new InProcProvider();

            _switchTypes = FeatureSwitching.LoadFeatureSwitchTypesFromAssembly(Assembly.GetExecutingAssembly());
            _switchTypes.AddRange(config.CustomSwitchTypes);

            _switches = FeatureSwitching.LoadFeatureSwitchList(config.FeatureSwitchEnumType, Assembly.GetCallingAssembly());

            // Inialise the provider.
            _provider.Initialise(_switches.Values.ToList());

            _initialised = true;

            return;
        }

        /// <summary>
        /// Is a feature switch turned on?
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static bool IsFeatureOn( int id )
        {
            if (!_initialised)
            {
                // TODO: Log an exception to indicate Femah wasn't initialised.
                return false;
            }

            if (!_switches.ContainsKey(id))
            {
                return false;
            }

            var featureSwitchName = _switches[id];

            try
            {
                var featureSwitch = _provider.Get(featureSwitchName);

                if (!featureSwitch.IsEnabled)
                {
                    return false;
                }

                IFemahContext context = _contextFactory.GenerateContext();
            
                return featureSwitch.IsOn(context);
            }
            catch (Exception e)
            {
                LogException(featureSwitchName, e);
                return false;
            }
        }

        /// <summary>
        /// Configure Femah fluently.
        /// </summary>
        /// <returns>A fluen configuration object.</returns>
        public static FemahFluentConfiguration Configure()
        {
            return new FemahFluentConfiguration();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Enable/disable a feature switch.
        /// </summary>
        /// <param name="name">Name of the feature switch.</param>
        /// <param name="enabled">True to enable the feature switch, false to disable.</param>
        internal static void EnableFeature(string name, bool enabled)
        {
            var featureSwitch = _provider.Get(name);
            if (featureSwitch != null)
            {
                featureSwitch.IsEnabled = enabled;
                _provider.Save(featureSwitch);
            }
        }

        /// <summary>
        /// Set the type of a feature switch.
        /// </summary>
        /// <param name="name">Name of the feature switch.</param>
        /// <param name="typeName">Name of the type of the feature switch type.</param>
        internal static void SetSwitchType(string name, string typeName)
        {
            // Load feature switch.
            var featureSwitch = _provider.Get(name);

            // Get type based on assembly qualified name.
            var type = _switchTypes.FirstOrDefault( t => String.Equals(t.AssemblyQualifiedName, typeName) );

            if ( featureSwitch == null || type == null )
            {
                return;
            }
            
            // Create instance of new type and copy standard IFeatureSwitch values across.
            var newFeatureSwitch = Activator.CreateInstance(type) as IFeatureSwitch;
            if (newFeatureSwitch != null)
            {
                newFeatureSwitch.Name = featureSwitch.Name;
                newFeatureSwitch.IsEnabled = featureSwitch.IsEnabled;
                newFeatureSwitch.FeatureType = featureSwitch.GetType().Name;
            }

            // Save as the new type of feature switch.
            _provider.Save(newFeatureSwitch);
        }

        /// <summary>
        /// Get a list of all available feature switches.
        /// </summary>
        /// <returns></returns>
        internal static List<IFeatureSwitch> AllFeatures()
        {
            return _provider.All();
        }

        /// <summary>
        /// Get a list of all available feature switch types.
        /// </summary>
        /// <returns></returns>
        internal static List<Type> AllSwitchTypes()
        {
            return _switchTypes;
        }

        /// <summary>
        /// Get a feature switch by name.
        /// </summary>
        /// <param name="featureName">Name of the feature switch.</param>
        /// <returns>An IFeatureSwitch for the feature switch.</returns>
        internal static IFeatureSwitch GetFeature(string featureName)
        {
            return _provider.Get(featureName);
        }

        /// <summary>
        /// Set the attributes for a feature switch.
        /// </summary>
        /// <param name="featureName">Name of the feature switch.</param>
        /// <param name="values">A list of name-value pairs that represent the attributes.</param>
        internal static void SetFeatureAttributes(string featureName, NameValueCollection values)
        {
            var feature = _provider.Get(featureName);
            if (feature != null)
            {
                feature.SetCustomAttributes(values);
                _provider.Save(feature);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Load the list of feature switch names from the specified type or, if that is null, 
        /// scan the specified assembly for an enum called "FemahFeatureSwitches"
        /// </summary>
        /// <param name="config"></param>
        /// <param name="assembly"></param>
        /// <returns></returns>
        private static Dictionary<int,string> LoadFeatureSwitchList(Type type, Assembly assembly)
        {
            var featureList = new Dictionary<int,string>();

            if (type != null)
            {
                var names = Enum.GetNames(type);
                var values = Enum.GetValues(type);

                for (int i = 0; i < names.Count(); i++)
                {
                    featureList.Add((int)(values.GetValue(i)), names[i]);
                }
            }
            else
            {
                var types = assembly.GetExportedTypes();
                var featureSwitches = types.FirstOrDefault(t => String.Equals(t.Name, "FemahFeatureSwitches", StringComparison.InvariantCulture));
                if (featureSwitches != null)
                {
                    var names = Enum.GetNames(featureSwitches);
                    var values = Enum.GetValues(featureSwitches);

                    for (int i = 0; i < names.Count(); i++)
                    {
                        featureList.Add((int)(values.GetValue(i)), names[i]);
                    }
                }
            }

            return featureList;
        }

        private static List<Type> LoadFeatureSwitchTypesFromAssembly(Assembly assembly)
        {
            var typeList = new List<Type>();

            var types = assembly.GetExportedTypes();
            foreach (var t in types)
            {
                if (t.GetInterfaces().Contains(typeof(IFeatureSwitch)) && !t.IsAbstract)
                {
                    typeList.Add(t);
                }
            }

            return typeList;
        }

       
        /// <summary>
        /// Log that an exception was thrown while testing if a particular feature switch was on.
        /// </summary>
        /// <param name="feature">Name of the feature switch</param>
        /// <param name="e">The exception that was thrown.</param>
        private static void LogException( string feature, Exception e )
        {
            // TODO - Log exceptions according to feature name.
        }

        #endregion
    }
}
