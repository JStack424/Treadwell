using System;

namespace Treadwell.Core
{
    public enum TerrainBrushKind
    {
        None = 0,
        LevelGround = 1,
        Pathen = 2,
        PavedRoad = 3
    }

    public readonly struct TerrainBrushShape
    {
        public TerrainBrushShape(
            int pieceCount,
            bool hasRootPiece,
            int terrainModifierCount,
            bool level,
            bool smooth,
            bool paintCleared,
            int paintType,
            bool hasStationRequirement,
            int resourceRequirementCount,
            int singleUnitResourceRequirementCount,
            int singleUnitStoneResourceRequirementCount)
        {
            PieceCount = pieceCount;
            HasRootPiece = hasRootPiece;
            TerrainModifierCount = terrainModifierCount;
            Level = level;
            Smooth = smooth;
            PaintCleared = paintCleared;
            PaintType = paintType;
            HasStationRequirement = hasStationRequirement;
            ResourceRequirementCount = resourceRequirementCount;
            SingleUnitResourceRequirementCount = singleUnitResourceRequirementCount;
            SingleUnitStoneResourceRequirementCount = singleUnitStoneResourceRequirementCount;
        }

        public int PieceCount { get; }
        public bool HasRootPiece { get; }
        public int TerrainModifierCount { get; }
        public bool Level { get; }
        public bool Smooth { get; }
        public bool PaintCleared { get; }
        public int PaintType { get; }
        public bool HasStationRequirement { get; }
        public int ResourceRequirementCount { get; }
        public int SingleUnitResourceRequirementCount { get; }
        public int SingleUnitStoneResourceRequirementCount { get; }
    }

    public static class VanillaTerrainBrushClassifier
    {
        public const int DirtPaintType = 0;
        public const int PavedPaintType = 2;

        public static TerrainBrushKind Classify(TerrainBrushShape shape)
        {
            if (shape.PieceCount != 1 || !shape.HasRootPiece || shape.TerrainModifierCount != 1)
                return TerrainBrushKind.None;

            if (!shape.HasStationRequirement && shape.ResourceRequirementCount == 0)
            {
                if (shape.Level)
                    return TerrainBrushKind.LevelGround;

                if (shape.PaintCleared && shape.PaintType == DirtPaintType)
                    return TerrainBrushKind.Pathen;
            }

            if (shape.PaintCleared && shape.PaintType == PavedPaintType &&
                shape.HasStationRequirement && shape.ResourceRequirementCount == 1 &&
                shape.SingleUnitResourceRequirementCount == 1 &&
                shape.SingleUnitStoneResourceRequirementCount == 1)
            {
                return TerrainBrushKind.PavedRoad;
            }

            return TerrainBrushKind.None;
        }
    }

    public readonly struct TerrainRadiusSelection
    {
        public const float VanillaRadius = 2f;
        public const float MinimumRadius = 1f;
        public const float MaximumRadius = 10f;
        public const float Step = 0.5f;

        public TerrainRadiusSelection(float radius)
        {
            Radius = ClampAndQuantize(radius);
        }

        public float Radius { get; }

        public TerrainRadiusSelection Scroll(float wheelDelta)
        {
            if (float.IsNaN(wheelDelta) || wheelDelta == 0f) return this;
            return new TerrainRadiusSelection(Radius + (wheelDelta > 0f ? Step : -Step));
        }

        public float ScaleFor(float vanillaRadius)
        {
            if (float.IsNaN(vanillaRadius) || float.IsInfinity(vanillaRadius) || vanillaRadius <= 0f)
                return 1f;
            return Radius / vanillaRadius;
        }

        private static float ClampAndQuantize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return VanillaRadius;
            var clamped = Math.Max(MinimumRadius, Math.Min(MaximumRadius, value));
            var steps = (float)Math.Round((clamped - MinimumRadius) / Step, MidpointRounding.AwayFromZero);
            return MinimumRadius + steps * Step;
        }
    }

    public readonly struct TerrainRadiusValues
    {
        public TerrainRadiusValues(float level, float smooth, float paint)
        {
            Level = level;
            Smooth = smooth;
            Paint = paint;
        }

        public float Level { get; }
        public float Smooth { get; }
        public float Paint { get; }

        public float MaximumActiveRadius(bool levelActive, bool smoothActive, bool paintActive)
        {
            var maximum = 0f;
            if (levelActive) maximum = Math.Max(maximum, Level);
            if (smoothActive) maximum = Math.Max(maximum, Smooth);
            if (paintActive) maximum = Math.Max(maximum, Paint);
            return maximum;
        }

        public TerrainRadiusValues ScaleActive(
            bool levelActive,
            bool smoothActive,
            bool paintActive,
            float targetRadius)
        {
            var baseline = MaximumActiveRadius(levelActive, smoothActive, paintActive);
            if (baseline <= 0f || float.IsNaN(baseline) || float.IsInfinity(baseline)) return this;
            var scale = new TerrainRadiusSelection(targetRadius).ScaleFor(baseline);
            return new TerrainRadiusValues(
                levelActive ? Level * scale : Level,
                smoothActive ? Smooth * scale : Smooth,
                paintActive ? Paint * scale : Paint);
        }
    }
}
