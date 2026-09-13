#nullable disable
using System;
using BepInEx;
using BepInEx.Configuration;

namespace Treadwell
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jstack424.treadwell";
        public const string PluginName = "Treadwell";
        public const string PluginVersion = GeneratedBuildInfo.Version;

        private FeatureHost _features;

        private void Awake()
        {
            var modEnabled = Config.Bind("General", "Enable mod", true,
                "Master switch. A restart is required after changing this setting.");
            var starterEnabled = Config.Bind("Features", "Enable starter feature", false,
                "Safe no-op placeholder. Replace it when implementing the first real feature.");

            Logger.LogInfo(PluginName + " " + PluginVersion + " (" + GeneratedBuildInfo.Commit + ") loading.");
            if (!modEnabled.Value)
            {
                Logger.LogInfo(PluginName + " is disabled by configuration; no feature modules were started.");
                return;
            }

            _features = new FeatureHost(new IFeatureModule[]
            {
                new NoOpFeatureModule(starterEnabled, Logger)
            }, Logger);

            var compatibility = CompatibilityGate.Evaluate(_features);
            if (!compatibility.IsCompatible)
            {
                Logger.LogError("Compatibility gate failed. " + PluginName +
                    " is fully disabled before any gameplay hooks were installed: " + compatibility.Reason);
                _features.Dispose();
                _features = null;
                return;
            }

            try
            {
                _features.Start();
                Logger.LogInfo("Compatibility gate passed; configured feature modules are enabled.");
            }
            catch (Exception exception)
            {
                _features.Dispose();
                _features = null;
                Logger.LogError(PluginName + " disabled because feature installation failed: " + exception);
            }
        }

        private void OnDisable()
        {
            _features?.Stop();
        }

        private void OnDestroy()
        {
            _features?.Dispose();
            _features = null;
        }
    }
}
