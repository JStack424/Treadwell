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
            var percentageRange = new AcceptableValueRange<float>(0f, 100f);
            var modEnabled = Config.Bind("General", "Enable mod", true,
                "Master switch for all Treadwell road bonuses.");
            var dirtSpeed = Config.Bind("Road bonuses", "Dirt sprint speed bonus (%)", 10f,
                new ConfigDescription("Extra sprint speed on vanilla dirt paths.", percentageRange));
            var dirtStamina = Config.Bind("Road bonuses", "Dirt sprint stamina reduction (%)", 10f,
                new ConfigDescription("Reduction to sprint stamina drain on vanilla dirt paths.", percentageRange));
            var pavedSpeed = Config.Bind("Road bonuses", "Paved sprint speed bonus (%)", 20f,
                new ConfigDescription("Extra sprint speed on vanilla paved roads.", percentageRange));
            var pavedStamina = Config.Bind("Road bonuses", "Paved sprint stamina reduction (%)", 20f,
                new ConfigDescription("Reduction to sprint stamina drain on vanilla paved roads.", percentageRange));

            Logger.LogInfo(PluginName + " " + PluginVersion + " (" + GeneratedBuildInfo.Commit + ") loading.");
            _features = new FeatureHost(new IFeatureModule[]
            {
                new RoadFeatureModule(modEnabled, dirtSpeed, dirtStamina, pavedSpeed, pavedStamina, Logger)
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
