using System;

namespace Treadwell.Core
{
    public enum BuildRequirementCheck
    {
        CanBuild,
        IsKnown,
        CanAlmostBuild
    }

    /// <summary>
    /// Identifies the one pinned vanilla paved-road piece for the narrow station-range bypass.
    /// Any missing or changed identity signal fails closed to vanilla behavior.
    /// </summary>
    public static class PavedRoadPlacementPolicy
    {
        public const string VanillaPrefabName = "paved_road";
        public const string VanillaDisplayName = "$piece_pavedroad";
        public const string VanillaStationName = "$piece_stonecutter";

        public static bool ShouldIgnoreStationRange(
            bool enabled,
            BuildRequirementCheck check,
            string prefabName,
            string displayName,
            string stationName,
            bool isPavedTerrainModifier)
        {
            return enabled &&
                   check == BuildRequirementCheck.CanBuild &&
                   string.Equals(prefabName, VanillaPrefabName, StringComparison.Ordinal) &&
                   string.Equals(displayName, VanillaDisplayName, StringComparison.Ordinal) &&
                   string.Equals(stationName, VanillaStationName, StringComparison.Ordinal) &&
                   isPavedTerrainModifier;
        }
    }
}
