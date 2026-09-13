using System;

namespace Treadwell.Core
{
    public enum TerrainSurface
    {
        Natural,
        DirtPath,
        Cultivated,
        PavedRoad,
        NonTerrain
    }

    public enum RoadSurface
    {
        None,
        Dirt,
        Paved
    }

    /// <summary>Classifies Valheim's terrain-paint RGB channels without a Unity dependency.</summary>
    public static class TerrainClassifier
    {
        public const float MinimumPaintStrength = 0.10f;
        public const float DominanceMargin = 0.02f;

        public static TerrainSurface Classify(float red, float green, float blue)
        {
            if (!IsFinite(red) || !IsFinite(green) || !IsFinite(blue))
                return TerrainSurface.Natural;

            // Cultivation wins close calls so a dirt bonus is never granted on a
            // cultivated/path boundary. Exact ties between road paints are left
            // to the short hysteresis window instead of flapping every sample.
            if (green >= MinimumPaintStrength &&
                green >= red - DominanceMargin &&
                green >= blue - DominanceMargin)
                return TerrainSurface.Cultivated;

            if (blue >= MinimumPaintStrength &&
                blue > red + DominanceMargin &&
                blue > green + DominanceMargin)
                return TerrainSurface.PavedRoad;

            if (red >= MinimumPaintStrength &&
                red > green + DominanceMargin &&
                red > blue + DominanceMargin)
                return TerrainSurface.DirtPath;

            return TerrainSurface.Natural;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Validated percentage settings and their multiplicative effects.</summary>
    public sealed class RoadTuning
    {
        public RoadTuning(
            float dirtSpeedPercent,
            float dirtStaminaReductionPercent,
            float pavedSpeedPercent,
            float pavedStaminaReductionPercent)
        {
            DirtSpeedPercent = ClampPercent(dirtSpeedPercent);
            DirtStaminaReductionPercent = ClampPercent(dirtStaminaReductionPercent);
            PavedSpeedPercent = ClampPercent(pavedSpeedPercent);
            PavedStaminaReductionPercent = ClampPercent(pavedStaminaReductionPercent);
        }

        public float DirtSpeedPercent { get; }
        public float DirtStaminaReductionPercent { get; }
        public float PavedSpeedPercent { get; }
        public float PavedStaminaReductionPercent { get; }

        public float SpeedMultiplier(RoadSurface surface)
        {
            switch (surface)
            {
                case RoadSurface.Dirt: return 1f + DirtSpeedPercent / 100f;
                case RoadSurface.Paved: return 1f + PavedSpeedPercent / 100f;
                default: return 1f;
            }
        }

        public float StaminaMultiplier(RoadSurface surface)
        {
            switch (surface)
            {
                case RoadSurface.Dirt: return 1f - DirtStaminaReductionPercent / 100f;
                case RoadSurface.Paved: return 1f - PavedStaminaReductionPercent / 100f;
                default: return 1f;
            }
        }

        public static float ClampPercent(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value)) return 0f;
            if (float.IsPositiveInfinity(value)) return 100f;
            if (value < 0f) return 0f;
            return value > 100f ? 100f : value;
        }
    }

    /// <summary>
    /// Keeps the last road classification across a tiny natural-terrain gap.
    /// Explicit cultivated soil and non-terrain colliders clear immediately.
    /// </summary>
    public sealed class RoadSurfaceTracker
    {
        private readonly double _naturalGapHoldSeconds;
        private RoadSurface _active;
        private double _lastRoadTime = double.NegativeInfinity;
        private double _lastObservationTime = double.NegativeInfinity;

        public RoadSurfaceTracker(double naturalGapHoldSeconds)
        {
            if (double.IsNaN(naturalGapHoldSeconds) || double.IsInfinity(naturalGapHoldSeconds) || naturalGapHoldSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(naturalGapHoldSeconds));
            _naturalGapHoldSeconds = naturalGapHoldSeconds;
        }

        public RoadSurface Observe(TerrainSurface observed, double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds) || nowSeconds < _lastObservationTime)
            {
                Reset();
                return RoadSurface.None;
            }

            _lastObservationTime = nowSeconds;
            if (observed == TerrainSurface.DirtPath || observed == TerrainSurface.PavedRoad)
            {
                _active = observed == TerrainSurface.DirtPath ? RoadSurface.Dirt : RoadSurface.Paved;
                _lastRoadTime = nowSeconds;
                return _active;
            }

            if (observed == TerrainSurface.Cultivated || observed == TerrainSurface.NonTerrain)
            {
                ResetAt(nowSeconds);
                return RoadSurface.None;
            }

            if (_active != RoadSurface.None && nowSeconds - _lastRoadTime <= _naturalGapHoldSeconds)
                return _active;

            ResetAt(nowSeconds);
            return RoadSurface.None;
        }

        public void Reset() => ResetAt(double.NegativeInfinity);

        private void ResetAt(double observationTime)
        {
            _active = RoadSurface.None;
            _lastRoadTime = double.NegativeInfinity;
            _lastObservationTime = observationTime;
        }
    }
}
