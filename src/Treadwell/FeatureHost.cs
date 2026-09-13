#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;

namespace Treadwell
{
    internal sealed class FeatureHost : IDisposable
    {
        private readonly IReadOnlyList<IFeatureModule> _modules;
        private readonly ManualLogSource _log;
        private bool _started;

        internal FeatureHost(IEnumerable<IFeatureModule> modules, ManualLogSource log)
        {
            _modules = (modules ?? throw new ArgumentNullException(nameof(modules))).ToArray();
            _log = log ?? throw new ArgumentNullException(nameof(log));
            if (_modules.Select(module => module.Id).Distinct(StringComparer.Ordinal).Count() != _modules.Count)
                throw new InvalidOperationException("Feature module ids must be unique.");
        }

        internal void ValidateCompatibility(ICollection<string> failures)
        {
            foreach (var module in _modules) module.ValidateCompatibility(failures);
        }

        internal void Start()
        {
            if (_started) return;
            try
            {
                foreach (var module in _modules)
                {
                    module.Enabled.SettingChanged += OnSettingChanged;
                    if (module.Enabled.Value) module.Enable();
                }
                _started = true;
            }
            catch
            {
                Stop();
                throw;
            }
        }

        private void OnSettingChanged(object sender, EventArgs eventArgs)
        {
            try
            {
                foreach (var module in _modules)
                {
                    if (module.Enabled.Value) module.Enable();
                    else module.Disable();
                }
            }
            catch (Exception exception)
            {
                _log.LogError("A feature toggle failed; all feature modules were disabled: " + exception);
                Stop();
            }
        }

        internal void Stop()
        {
            foreach (var module in _modules.Reverse())
            {
                module.Enabled.SettingChanged -= OnSettingChanged;
                try { module.Disable(); }
                catch (Exception exception) { _log.LogError("Feature cleanup failed for " + module.Id + ": " + exception); }
            }
            _started = false;
        }

        public void Dispose() => Stop();
    }
}
