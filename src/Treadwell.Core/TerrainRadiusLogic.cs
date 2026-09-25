using System;
using System.Collections.Generic;

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

            if (MatchesPieceIdentity(shape, "$piece_levelground") &&
                shape.Smooth && shape.PaintType == DirtPaintType &&
                !shape.HasStationRequirement && shape.ResourceRequirementCount == 0)
            {
                return TerrainBrushKind.LevelGround;
            }

            if (MatchesPieceIdentity(shape, "$piece_path") &&
                !shape.Smooth && shape.PaintType == DirtPaintType &&
                !shape.HasStationRequirement && shape.ResourceRequirementCount == 0)
            {
                return TerrainBrushKind.Pathen;
            }

            if (MatchesPieceIdentity(shape, "$piece_pavedroad") &&
                shape.Smooth && shape.PaintType == PavedPaintType &&
                shape.HasStationRequirement && shape.ResourceRequirementCount == 1 &&
                shape.SingleUnitResourceRequirementCount == 1 &&
                shape.SingleUnitStoneResourceRequirementCount == 1)
            {
                return TerrainBrushKind.PavedRoad;
            }

            return TerrainBrushKind.None;
        }

        private static bool MatchesPieceIdentity(TerrainBrushShape shape, string pieceName)
        {
            // The active vanilla table is authoritative. Valheim has changed internal
            // prefab object names while retaining the localized action identity and
            // semantic TerrainOp/recipe shape, so prefab names are diagnostic only.
            return string.Equals(shape.PieceName, pieceName, StringComparison.Ordinal);
        }
    }

    public readonly struct TerrainRadiusSelection
    {
        public const float MinimumRadius = 1f;
        public const float MaximumRadius = 10f;
        public const float LevelGroundMaximumRadius = 8f;
        public const float Step = 0.5f;

        private readonly float _maximumRadius;

        public TerrainRadiusSelection(float radius)
            : this(radius, MaximumRadius)
        {
        }

        public TerrainRadiusSelection(TerrainBrushKind kind, float radius)
            : this(radius, MaximumFor(kind))
        {
        }

        private TerrainRadiusSelection(float radius, float maximumRadius)
        {
            _maximumRadius = maximumRadius;
            Radius = ClampAndQuantize(radius, 2f, maximumRadius);
        }

        public float Radius { get; }
        public float Maximum => _maximumRadius;

        public TerrainRadiusSelection Scroll(float wheelDelta)
        {
            if (float.IsNaN(wheelDelta) || wheelDelta == 0f) return this;
            return new TerrainRadiusSelection(Radius + (wheelDelta > 0f ? Step : -Step), _maximumRadius);
        }

        public static float MaximumFor(TerrainBrushKind kind)
        {
            return kind == TerrainBrushKind.LevelGround ? LevelGroundMaximumRadius : MaximumRadius;
        }

        public float ScaleFor(float vanillaRadius)
        {
            if (!IsFinitePositive(vanillaRadius)) return 1f;
            return Radius / vanillaRadius;
        }

        private static float ClampAndQuantize(float value, float fallback, float maximumRadius)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            var clamped = Math.Max(MinimumRadius, Math.Min(maximumRadius, value));
            var steps = (float)Math.Round((clamped - MinimumRadius) / Step, MidpointRounding.AwayFromZero);
            return MinimumRadius + steps * Step;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }

    public static class CameraWheelRouting
    {
        public static bool ShouldSuppressZoom(
            bool isLocalPlayer,
            bool inPlaceMode,
            bool isDead,
            bool isVanillaHoeTable,
            bool altHeld,
            bool pieceSelectionVisible)
        {
            return isLocalPlayer && inPlaceMode && !isDead && isVanillaHoeTable &&
                   altHeld && !pieceSelectionVisible;
        }
    }

    public static class TerrainIndicatorRouting
    {
        public static bool CanSynchronize(bool markerExists, bool activeInHierarchy)
        {
            // Vanilla terrain prefabs serialize _GhostOnly inactive. Its Transform is
            // still safe to scale before Player.SetupPlacementGhost activates it.
            _ = activeInHierarchy;
            return markerExists;
        }

        public static bool ShouldScaleOwnTransform(bool isGhostOnlyMarker, bool isLocalScalingParticle)
        {
            // A ParticleSystem in Local scaling mode ignores ancestor scale. Scale its
            // own Transform unless it already shares the _GhostOnly marker Transform.
            return isGhostOnlyMarker || isLocalScalingParticle;
        }
    }

    public interface ITerrainRadiusMutationTarget
    {
        object Identity { get; }
        bool LevelActive { get; }
        bool RaiseActive { get; }
        bool SmoothActive { get; }
        bool PaintActive { get; }
        float LevelRadius { get; set; }
        float RaiseRadius { get; set; }
        float SmoothRadius { get; set; }
        float PaintRadius { get; set; }
    }

    public sealed class TerrainRadiusMutationSession
    {
        private readonly List<TargetState> _targets = new List<TargetState>();

        public TerrainRadiusMutationSession(IEnumerable<ITerrainRadiusMutationTarget> targets)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            foreach (var target in targets)
            {
                if (target == null || target.Identity == null)
                    throw new ArgumentNullException(nameof(targets));

                var duplicate = false;
                foreach (var existing in _targets)
                {
                    if (!ReferenceEquals(existing.Target.Identity, target.Identity)) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) _targets.Add(new TargetState(target));
            }
            if (_targets.Count == 0) throw new ArgumentException("At least one terrain-radius target is required.", nameof(targets));
        }

        public int TargetCount => _targets.Count;

        public void Apply(float targetRadius)
        {
            try
            {
                foreach (var target in _targets) target.Apply(targetRadius);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        public bool Restore()
        {
            var conflict = false;
            foreach (var target in _targets) conflict |= target.Restore();
            return conflict;
        }

        private sealed class TargetState
        {
            private const float Epsilon = 0.0001f;
            private readonly bool _levelActive;
            private readonly bool _raiseActive;
            private readonly bool _smoothActive;
            private readonly bool _paintActive;
            private readonly TerrainRadiusValues _original;
            private TerrainRadiusValues _applied;
            private bool _levelApplied;
            private bool _raiseApplied;
            private bool _smoothApplied;
            private bool _paintApplied;

            internal TargetState(ITerrainRadiusMutationTarget target)
            {
                Target = target;
                _levelActive = target.LevelActive;
                _raiseActive = target.RaiseActive;
                _smoothActive = target.SmoothActive;
                _paintActive = target.PaintActive;
                _original = new TerrainRadiusValues(
                    target.LevelRadius,
                    target.RaiseRadius,
                    target.SmoothRadius,
                    target.PaintRadius);
            }

            internal ITerrainRadiusMutationTarget Target { get; }

            internal void Apply(float targetRadius)
            {
                _applied = _original.ScaleActive(
                    _levelActive, _raiseActive, _smoothActive, _paintActive, targetRadius);
                if (_levelActive)
                {
                    Target.LevelRadius = _applied.Level;
                    _levelApplied = true;
                }
                if (_raiseActive)
                {
                    Target.RaiseRadius = _applied.Raise;
                    _raiseApplied = true;
                }
                if (_smoothActive)
                {
                    Target.SmoothRadius = _applied.Smooth;
                    _smoothApplied = true;
                }
                if (_paintActive)
                {
                    Target.PaintRadius = _applied.Paint;
                    _paintApplied = true;
                }
            }

            internal bool Restore()
            {
                var conflict = false;
                conflict |= RestoreField(_levelApplied, () => Target.LevelRadius, value => Target.LevelRadius = value, _applied.Level, _original.Level);
                conflict |= RestoreField(_raiseApplied, () => Target.RaiseRadius, value => Target.RaiseRadius = value, _applied.Raise, _original.Raise);
                conflict |= RestoreField(_smoothApplied, () => Target.SmoothRadius, value => Target.SmoothRadius = value, _applied.Smooth, _original.Smooth);
                conflict |= RestoreField(_paintApplied, () => Target.PaintRadius, value => Target.PaintRadius = value, _applied.Paint, _original.Paint);
                _levelApplied = false;
                _raiseApplied = false;
                _smoothApplied = false;
                _paintApplied = false;
                return conflict;
            }

            private static bool RestoreField(bool applied, Func<float> read, Action<float> write, float appliedValue, float originalValue)
            {
                if (!applied) return false;
                if (!Approximately(read(), appliedValue)) return true;
                write(originalValue);
                return false;
            }

            private static bool Approximately(float left, float right)
                => !float.IsNaN(left) && !float.IsNaN(right) && Math.Abs(left - right) <= Epsilon;
        }
    }

    public readonly struct TerrainIndicatorScale
    {
        public TerrainIndicatorScale(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public TerrainIndicatorScale ScaleUniformly(float factor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f) return this;
            return new TerrainIndicatorScale(X * factor, Y * factor, Z * factor);
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
