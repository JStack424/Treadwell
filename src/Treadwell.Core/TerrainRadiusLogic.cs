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
            int terrainOpCount,
            bool hasRootTerrainOp,
            int terrainModifierCount,
            string prefabName,
            string pieceName,
            bool canRotate,
            bool level,
            bool raise,
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
            TerrainOpCount = terrainOpCount;
            HasRootTerrainOp = hasRootTerrainOp;
            TerrainModifierCount = terrainModifierCount;
            PrefabName = prefabName;
            PieceName = pieceName;
            CanRotate = canRotate;
            Level = level;
            Raise = raise;
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
        public int TerrainOpCount { get; }
        public bool HasRootTerrainOp { get; }
        public int TerrainModifierCount { get; }
        public string PrefabName { get; }
        public string PieceName { get; }
        public bool CanRotate { get; }
        public bool Level { get; }
        public bool Raise { get; }
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
            if (shape.PieceCount != 1 || !shape.HasRootPiece || shape.TerrainOpCount != 1 ||
                !shape.HasRootTerrainOp || shape.TerrainModifierCount != 0 || shape.CanRotate)
                return TerrainBrushKind.None;

            if (shape.Level || shape.Raise || !shape.PaintCleared)
                return TerrainBrushKind.None;

            if (MatchesIdentity(shape, "mud_road_v2", "$piece_levelground") &&
                shape.Smooth && shape.PaintType == DirtPaintType &&
                !shape.HasStationRequirement && shape.ResourceRequirementCount == 0)
            {
                return TerrainBrushKind.LevelGround;
            }

            if (MatchesIdentity(shape, "path_v2", "$piece_path") &&
                !shape.Smooth && shape.PaintType == DirtPaintType &&
                !shape.HasStationRequirement && shape.ResourceRequirementCount == 0)
            {
                return TerrainBrushKind.Pathen;
            }

            if (MatchesIdentity(shape, "paved_road_v2", "$piece_pavedroad") &&
                shape.Smooth && shape.PaintType == PavedPaintType &&
                shape.HasStationRequirement && shape.ResourceRequirementCount == 1 &&
                shape.SingleUnitResourceRequirementCount == 1 &&
                shape.SingleUnitStoneResourceRequirementCount == 1)
            {
                return TerrainBrushKind.PavedRoad;
            }

            return TerrainBrushKind.None;
        }

        private static bool MatchesIdentity(TerrainBrushShape shape, string prefabName, string pieceName)
        {
            return string.Equals(shape.PrefabName, prefabName, StringComparison.Ordinal) &&
                   string.Equals(shape.PieceName, pieceName, StringComparison.Ordinal);
        }
    }

    public readonly struct TerrainRadiusSelection
    {
        public const float MinimumRadius = 1f;
        public const float MaximumRadius = 10f;
        public const float Step = 0.5f;

        public TerrainRadiusSelection(float radius)
        {
            Radius = ClampAndQuantize(radius, 2f);
        }

        public float Radius { get; }

        public TerrainRadiusSelection Scroll(float wheelDelta)
        {
            if (float.IsNaN(wheelDelta) || wheelDelta == 0f) return this;
            return new TerrainRadiusSelection(Radius + (wheelDelta > 0f ? Step : -Step));
        }

        public float ScaleFor(float vanillaRadius)
        {
            if (!IsFinitePositive(vanillaRadius)) return 1f;
            return Radius / vanillaRadius;
        }

        private static float ClampAndQuantize(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            var clamped = Math.Max(MinimumRadius, Math.Min(MaximumRadius, value));
            var steps = (float)Math.Round((clamped - MinimumRadius) / Step, MidpointRounding.AwayFromZero);
            return MinimumRadius + steps * Step;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }

    public readonly struct TerrainRadiusValues
    {
        public TerrainRadiusValues(float level, float raise, float smooth, float paint)
        {
            Level = level;
            Raise = raise;
            Smooth = smooth;
            Paint = paint;
        }

        public float Level { get; }
        public float Raise { get; }
        public float Smooth { get; }
        public float Paint { get; }

        public float MaximumActiveRadius(bool levelActive, bool raiseActive, bool smoothActive, bool paintActive)
        {
            var maximum = 0f;
            if (levelActive) maximum = Math.Max(maximum, Level);
            if (raiseActive) maximum = Math.Max(maximum, Raise);
            if (smoothActive) maximum = Math.Max(maximum, Smooth);
            if (paintActive) maximum = Math.Max(maximum, Paint);
            return maximum;
        }

        public TerrainRadiusValues ScaleActive(
            bool levelActive,
            bool raiseActive,
            bool smoothActive,
            bool paintActive,
            float targetRadius)
        {
            var baseline = MaximumActiveRadius(levelActive, raiseActive, smoothActive, paintActive);
            if (baseline <= 0f || float.IsNaN(baseline) || float.IsInfinity(baseline)) return this;
            var scale = new TerrainRadiusSelection(targetRadius).ScaleFor(baseline);
            return new TerrainRadiusValues(
                levelActive ? Level * scale : Level,
                raiseActive ? Raise * scale : Raise,
                smoothActive ? Smooth * scale : Smooth,
                paintActive ? Paint * scale : Paint);
        }
    }
}
