#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Treadwell.Core;
using UnityEngine;

namespace Treadwell
{
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
            try
            {
                InstallPatches();
                _active = true;
                Log.LogInfo("Enabled feature module: " + Id);
            }
            catch
            {
                _harmony.UnpatchSelf();
                OnDisabled();
                throw;
            }
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

    internal sealed class RoadFeatureModule : FeatureModuleBase
    {
        internal const double NaturalGapHoldSeconds = 0.18d;
        private static RoadFeatureModule _activeModule;

        private static readonly MethodInfo GetLastGroundColliderMethod =
            AccessTools.DeclaredMethod(typeof(Character), "GetLastGroundCollider", Type.EmptyTypes);
        private static readonly MethodInfo HaveBuildStationInRangeMethod =
            AccessTools.DeclaredMethod(typeof(CraftingStation), "HaveBuildStationInRange",
                new[] { typeof(string), typeof(Vector3) });
        private static readonly MethodInfo UnityObjectImplicitMethod =
            AccessTools.Method(typeof(UnityEngine.Object), "op_Implicit", new[] { typeof(UnityEngine.Object) });
        private static readonly MethodInfo AdjustStationSatisfiedMethod =
            AccessTools.DeclaredMethod(typeof(RoadFeatureModule), nameof(AdjustStationSatisfied),
                new[] { typeof(bool), typeof(Piece), typeof(Player.RequirementMode) });

        private readonly ConfigEntry<bool> _pavedRoadWithoutStonecutter;
        private readonly ConfigEntry<float> _dirtSpeed;
        private readonly ConfigEntry<float> _dirtStamina;
        private readonly ConfigEntry<float> _pavedSpeed;
        private readonly ConfigEntry<float> _pavedStamina;
        private readonly RoadSurfaceTracker _surfaceTracker = new RoadSurfaceTracker(NaturalGapHoldSeconds);

        internal RoadFeatureModule(
            ConfigEntry<bool> enabled,
            ConfigEntry<bool> pavedRoadWithoutStonecutter,
            ConfigEntry<float> dirtSpeed,
            ConfigEntry<float> dirtStamina,
            ConfigEntry<float> pavedSpeed,
            ConfigEntry<float> pavedStamina,
            ManualLogSource log)
            : base("roads", enabled, log)
        {
            _pavedRoadWithoutStonecutter = pavedRoadWithoutStonecutter ?? throw new ArgumentNullException(nameof(pavedRoadWithoutStonecutter));
            _dirtSpeed = dirtSpeed ?? throw new ArgumentNullException(nameof(dirtSpeed));
            _dirtStamina = dirtStamina ?? throw new ArgumentNullException(nameof(dirtStamina));
            _pavedSpeed = pavedSpeed ?? throw new ArgumentNullException(nameof(pavedSpeed));
            _pavedStamina = pavedStamina ?? throw new ArgumentNullException(nameof(pavedStamina));
        }

        public override void ValidateCompatibility(ICollection<string> failures)
        {
            CompatibilityGate.RequireMethod(failures, typeof(Player), "HaveRequirements", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(Piece), typeof(Player.RequirementMode) }, method => !method.IsStatic);
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_name", typeof(string),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_craftingStation", typeof(CraftingStation),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(CraftingStation), "m_name", typeof(string),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireMethod(failures, typeof(CraftingStation), "HaveBuildStationInRange", typeof(CraftingStation),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(string), typeof(Vector3) }, method => method.IsStatic);
            CompatibilityGate.RequireField(failures, typeof(TerrainModifier), "m_paintType", typeof(TerrainModifier.PaintType),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "GetRunSpeedFactor", typeof(float),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => method.IsFamily && method.IsVirtual);
            CompatibilityGate.RequireMethod(failures, typeof(SEMan), "ModifyRunStaminaDrain", typeof(void),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(float), typeof(float).MakeByRefType(), typeof(Vector3), typeof(bool) },
                method => !method.IsStatic && string.Equals(method.GetParameters()[1].Name, "drain", StringComparison.Ordinal));
            CompatibilityGate.RequireMethod(failures, typeof(Character), "IsOnGround", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes);
            CompatibilityGate.RequireMethod(failures, typeof(Character), "IsRunning", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes);
            CompatibilityGate.RequireMethodNamedReturn(failures, typeof(Character), "GetLastGroundCollider", "UnityEngine.Collider",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes);
            CompatibilityGate.RequireMethod(failures, typeof(Heightmap), "GetPaintMask", typeof(Color),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, new[] { typeof(Vector3) });
            CompatibilityGate.RequireField(failures, typeof(Player), "m_localPlayer", typeof(Player),
                BindingFlags.Static | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(SEMan), "m_character", typeof(Character),
                BindingFlags.Instance | BindingFlags.NonPublic);
            CompatibilityGate.RequireField(failures, typeof(Heightmap), "m_paintMaskDirt", typeof(Color),
                BindingFlags.Static | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Heightmap), "m_paintMaskCultivated", typeof(Color),
                BindingFlags.Static | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Heightmap), "m_paintMaskPaved", typeof(Color),
                BindingFlags.Static | BindingFlags.Public);
            CompatibilityGate.RequireColor(failures, "dirt paint", Heightmap.m_paintMaskDirt, 1f, 0f, 0f, 1f);
            CompatibilityGate.RequireColor(failures, "cultivated paint", Heightmap.m_paintMaskCultivated, 0f, 1f, 0f, 1f);
            CompatibilityGate.RequireColor(failures, "paved paint", Heightmap.m_paintMaskPaved, 0f, 0f, 1f, 1f);
        }

        protected override void InstallPatches()
        {
            if (_activeModule != null && !ReferenceEquals(_activeModule, this))
                throw new InvalidOperationException("Another road feature module is already active.");

            _activeModule = this;
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(Player), "HaveRequirements",
                    new[] { typeof(Piece), typeof(Player.RequirementMode) }),
                transpiler: new HarmonyMethod(typeof(RoadFeatureModule), nameof(HaveRequirementsTranspiler)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(Player), "GetRunSpeedFactor", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(GetRunSpeedFactorPostfix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(SEMan), "ModifyRunStaminaDrain",
                    new[] { typeof(float), typeof(float).MakeByRefType(), typeof(Vector3), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(ModifyRunStaminaDrainPostfix)));
        }

        protected override void OnDisabled()
        {
            if (ReferenceEquals(_activeModule, this)) _activeModule = null;
            _surfaceTracker.Reset();
        }

        private static IEnumerable<CodeInstruction> HaveRequirementsTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var patched = new List<CodeInstruction>(instructions);
            var insertAt = -1;
            for (var index = 0; index + 1 < patched.Count; index++)
            {
                if (!patched[index].Calls(HaveBuildStationInRangeMethod) ||
                    !patched[index + 1].Calls(UnityObjectImplicitMethod))
                    continue;
                if (insertAt >= 0)
                    throw new InvalidOperationException("Player.HaveRequirements contains multiple station-range checks.");
                insertAt = index + 2;
            }

            if (insertAt < 0)
                throw new InvalidOperationException("Player.HaveRequirements station-range check is not the verified build.");
            if (insertAt >= patched.Count || patched[insertAt].labels.Count != 0 || patched[insertAt].blocks.Count != 0)
                throw new InvalidOperationException("Player.HaveRequirements station-range result crosses a control-flow boundary.");

            patched.InsertRange(insertAt, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, AdjustStationSatisfiedMethod)
            });
            return patched;
        }

        private static bool AdjustStationSatisfied(
            bool stationSatisfied,
            Piece piece,
            Player.RequirementMode mode)
        {
            if (stationSatisfied) return true;

            var module = _activeModule;
            if (module == null || piece == null || piece.m_craftingStation == null) return false;
            var check = ToBuildRequirementCheck(mode);
            if (!check.HasValue) return false;

            var terrainModifier = piece.GetComponent<TerrainModifier>();
            return PavedRoadPlacementPolicy.ShouldIgnoreStationRange(
                module._pavedRoadWithoutStonecutter.Value,
                check.Value,
                piece.gameObject != null ? piece.gameObject.name : null,
                piece.m_name,
                piece.m_craftingStation.m_name,
                terrainModifier != null && terrainModifier.m_paintType == TerrainModifier.PaintType.Paved);
        }

        private static BuildRequirementCheck? ToBuildRequirementCheck(Player.RequirementMode mode)
        {
            switch (mode)
            {
                case Player.RequirementMode.CanBuild: return BuildRequirementCheck.CanBuild;
                case Player.RequirementMode.IsKnown: return BuildRequirementCheck.IsKnown;
                case Player.RequirementMode.CanAlmostBuild: return BuildRequirementCheck.CanAlmostBuild;
                default: return null;
            }
        }

        private static void GetRunSpeedFactorPostfix(Player __instance, ref float __result)
        {
            var module = _activeModule;
            if (module == null || __instance == null || __instance != Player.m_localPlayer || !__instance.IsRunning()) return;
            __result *= module.CurrentTuning().SpeedMultiplier(module.ResolveSurface(__instance));
        }

        private static void ModifyRunStaminaDrainPostfix(Character ___m_character, ref float drain)
        {
            var module = _activeModule;
            var player = ___m_character as Player;
            if (module == null || player == null || player != Player.m_localPlayer) return;
            drain *= module.CurrentTuning().StaminaMultiplier(module.ResolveSurface(player));
        }

        private RoadTuning CurrentTuning()
            => new RoadTuning(_dirtSpeed.Value, _dirtStamina.Value, _pavedSpeed.Value, _pavedStamina.Value);

        private RoadSurface ResolveSurface(Player player)
        {
            var now = (double)Time.unscaledTime;
            if (!player.IsOnGround()) return _surfaceTracker.Observe(TerrainSurface.NonTerrain, now);

            var collider = GetLastGroundColliderMethod?.Invoke(player, null) as Component;
            if (collider == null) return _surfaceTracker.Observe(TerrainSurface.NonTerrain, now);

            var heightmap = collider.GetComponent<Heightmap>() ?? collider.GetComponentInParent<Heightmap>();
            if (heightmap == null) return _surfaceTracker.Observe(TerrainSurface.NonTerrain, now);

            var paint = heightmap.GetPaintMask(player.transform.position);
            var observed = TerrainClassifier.Classify(paint.r, paint.g, paint.b);
            return _surfaceTracker.Observe(observed, now);
        }
    }
}
