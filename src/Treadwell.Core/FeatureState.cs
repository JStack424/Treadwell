using System;

namespace Treadwell.Core
{
    /// <summary>Pure state used as a starter seam for behavior tests.</summary>
    public sealed class FeatureState
    {
        public FeatureState(bool enabled)
        {
            Enabled = enabled;
        }

        public bool Enabled { get; }

        public FeatureState WithEnabled(bool enabled)
            => enabled == Enabled ? this : new FeatureState(enabled);

        public override bool Equals(object? obj)
            => obj is FeatureState other && other.Enabled == Enabled;

        public override int GetHashCode() => Enabled ? 1 : 0;
    }
}
