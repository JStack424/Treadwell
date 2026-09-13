#nullable disable
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Treadwell
{
    /// <summary>Each cohesive feature owns its config, validation, patches, and cleanup.</summary>
    internal interface IFeatureModule
    {
        string Id { get; }
        ConfigEntry<bool> Enabled { get; }
        void ValidateCompatibility(ICollection<string> failures);
        void Enable();
        void Disable();
    }

    internal abstract class FeatureModuleBase : IFeatureModule
    {
        private readonly Harmony _harmony;
        private bool _active;

        protected FeatureModuleBase(string id, ConfigEntry<bool> enabled, ManualLogSource log)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Feature id is required.", nameof(id));
            Id = id;
            Enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            Log = log ?? throw new ArgumentNullException(nameof(log));
            _harmony = new Harmony(Plugin.PluginGuid + ".feature." + id);
        }

        public string Id { get; }
        public ConfigEntry<bool> Enabled { get; }
        protected ManualLogSource Log { get; }
        protected Harmony Harmony => _harmony;

        public abstract void ValidateCompatibility(ICollection<string> failures);

        public void Enable()
        {
            if (_active) return;
            InstallPatches();
            _active = true;
            Log.LogInfo("Enabled feature module: " + Id);
        }

        public void Disable()
        {
            if (!_active) return;
            try { _harmony.UnpatchSelf(); }
            finally
            {
                _active = false;
                OnDisabled();
                Log.LogInfo("Disabled feature module: " + Id);
            }
        }

        protected abstract void InstallPatches();
        protected virtual void OnDisabled() { }
    }

    /// <summary>Safe placeholder: replace this class with the first real feature.</summary>
    internal sealed class NoOpFeatureModule : FeatureModuleBase
    {
        internal NoOpFeatureModule(ConfigEntry<bool> enabled, ManualLogSource log)
            : base("starter", enabled, log) { }

        public override void ValidateCompatibility(ICollection<string> failures)
        {
            // A real module must validate every reflected method/field it will use here.
        }

        protected override void InstallPatches()
        {
            // Intentionally empty. The generated mod changes no gameplay until implemented.
        }
    }
}
