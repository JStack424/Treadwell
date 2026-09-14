#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
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
        private static readonly MethodInfo UpdateAvailablePiecesListMethod =
            AccessTools.DeclaredMethod(typeof(Player), "UpdateAvailablePiecesList", Type.EmptyTypes);

        private readonly ConfigEntry<bool> _pavedRoadWithoutStonecutter;
        private readonly ConfigEntry<float> _dirtSpeed;
        private readonly ConfigEntry<float> _dirtStamina;
        private readonly ConfigEntry<float> _pavedSpeed;
        private readonly ConfigEntry<float> _pavedStamina;
        private readonly RoadSurfaceTracker _surfaceTracker = new RoadSurfaceTracker(NaturalGapHoldSeconds);
        private readonly PavedRoadStationOverride<Piece, CraftingStation> _stationOverride;
        private PieceTable _lastPieceTable;
        private bool _pavedSettingSubscribed;
        private bool _loggedStationRemoved;
        private bool _loggedStationAlreadyAbsent;
        private bool _loggedCandidateMissing;
        private bool _loggedStationConflict;

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
            _stationOverride = new PavedRoadStationOverride<Piece, CraftingStation>(
                piece => piece.gameObject != null ? piece.gameObject.name : null,
                piece => piece.m_name,
                piece => piece.m_craftingStation,
                (piece, station) => piece.m_craftingStation = station);
        }

        public override void ValidateCompatibility(ICollection<string> failures)
        {
            CompatibilityGate.RequireMethod(failures, typeof(PieceTable), "UpdateAvailable", typeof(void),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(HashSet<string>), typeof(Player), typeof(bool), typeof(bool) }, method => !method.IsStatic);
            CompatibilityGate.RequireField(failures, typeof(PieceTable), "m_pieces", typeof(List<GameObject>),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireMethod(failures, typeof(ZNetScene), "OnDestroy", typeof(void),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "SetPlaceMode", typeof(void),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                new[] { typeof(PieceTable) }, method => !method.IsStatic && method.IsFamily && method.IsVirtual);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "GetBuildTool", typeof(PieceTable),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "UpdateAvailablePiecesList", typeof(void),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "HaveRequirements", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(Piece), typeof(Player.RequirementMode) }, method => !method.IsStatic);
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_name", typeof(string),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_craftingStation", typeof(CraftingStation),
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
                AccessTools.DeclaredMethod(typeof(Player), "SetPlaceMode", new[] { typeof(PieceTable) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PlayerSetPlaceModePrefix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(PieceTable), "UpdateAvailable",
                    new[] { typeof(HashSet<string>), typeof(Player), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PieceTableUpdateAvailablePrefix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(Player), "HaveRequirements",
                    new[] { typeof(Piece), typeof(Player.RequirementMode) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PlayerHaveRequirementsPrefix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(ZNetScene), "OnDestroy", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(ZNetSceneOnDestroyPrefix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(Player), "GetRunSpeedFactor", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(GetRunSpeedFactorPostfix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(SEMan), "ModifyRunStaminaDrain",
                    new[] { typeof(float), typeof(float).MakeByRefType(), typeof(Vector3), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(ModifyRunStaminaDrainPostfix)));

            _pavedRoadWithoutStonecutter.SettingChanged += OnPavedRoadSettingChanged;
            _pavedSettingSubscribed = true;
            RefreshCurrentBuildPieces();
            RefreshPlayerAvailablePieces();
        }

        protected override void OnDisabled()
        {
            if (_pavedSettingSubscribed)
            {
                _pavedRoadWithoutStonecutter.SettingChanged -= OnPavedRoadSettingChanged;
                _pavedSettingSubscribed = false;
            }
            RestorePavedRoadStation();
            try { RefreshPlayerAvailablePieces(); }
            catch (Exception exception) { Log.LogWarning("Could not refresh vanilla build-piece availability during cleanup: " + exception); }
            _lastPieceTable = null;
            if (ReferenceEquals(_activeModule, this)) _activeModule = null;
            _surfaceTracker.Reset();
        }

        private static void PlayerSetPlaceModePrefix(PieceTable __0)
        {
            _activeModule?.RefreshPavedRoadStation(__0, reportMissingCandidate: true);
        }

        private static void PieceTableUpdateAvailablePrefix(PieceTable __instance)
        {
            _activeModule?.RefreshPavedRoadStation(__instance, reportMissingCandidate: false);
        }

        private static void PlayerHaveRequirementsPrefix(Piece __0)
        {
            _activeModule?.ApplyPavedRoadStationOverride(__0, reportNearMiss: true);
        }

        private static void ZNetSceneOnDestroyPrefix()
        {
            var module = _activeModule;
            if (module == null) return;
            module.RestorePavedRoadStation();
            module._lastPieceTable = null;
        }

        private void OnPavedRoadSettingChanged(object sender, EventArgs eventArgs)
        {
            try
            {
                if (_pavedRoadWithoutStonecutter.Value) RefreshCurrentBuildPieces();
                else RestorePavedRoadStation();
                RefreshPlayerAvailablePieces();
            }
            catch (Exception exception)
            {
                RestorePavedRoadStation();
                Log.LogError("Paved Road recipe update failed; the vanilla station requirement was restored: " + exception);
            }
        }

        private void RefreshCurrentBuildPieces()
        {
            if (!_pavedRoadWithoutStonecutter.Value)
            {
                RestorePavedRoadStation();
                return;
            }

            var player = Player.m_localPlayer;
            var activeTable = player != null ? player.GetBuildTool() : null;
            if (activeTable != null)
            {
                RefreshPavedRoadStation(activeTable, reportMissingCandidate: true);
                return;
            }

            if (_lastPieceTable != null)
                RefreshPavedRoadStation(_lastPieceTable, reportMissingCandidate: false);
        }

        private static void RefreshPlayerAvailablePieces()
        {
            var player = Player.m_localPlayer;
            if (player != null) UpdateAvailablePiecesListMethod.Invoke(player, null);
        }

        private void RefreshPavedRoadStation(PieceTable table, bool reportMissingCandidate)
        {
            if (table == null) return;
            if (!_pavedRoadWithoutStonecutter.Value)
            {
                RestorePavedRoadStation();
                return;
            }

            var piece = FindPavedRoadPiece(table, out var nearMatchFound);
            if (piece == null)
            {
                if (reportMissingCandidate && (nearMatchFound || IsLikelyHoePieceTable(table)))
                    LogCandidateMissingOnce();
                return;
            }

            _lastPieceTable = table;
            ApplyPavedRoadStationOverride(piece, reportNearMiss: false);
        }

        private void ApplyPavedRoadStationOverride(Piece piece, bool reportNearMiss)
        {
            if (!_pavedRoadWithoutStonecutter.Value || piece == null) return;

            var result = _stationOverride.Apply(piece);
            switch (result)
            {
                case StationOverrideApplyResult.Removed:
                    if (!_loggedStationRemoved)
                    {
                        _loggedStationRemoved = true;
                        Log.LogInfo("Paved Road station field removed; no nearby stonecutter is now required.");
                    }
                    break;
                case StationOverrideApplyResult.AlreadyAbsent:
                    if (!_loggedStationAlreadyAbsent)
                    {
                        _loggedStationAlreadyAbsent = true;
                        Log.LogInfo("Paved Road station field was already absent; no change was needed.");
                    }
                    break;
                case StationOverrideApplyResult.Conflict:
                    if (!_loggedStationConflict)
                    {
                        _loggedStationConflict = true;
                        Log.LogWarning("Paved Road station field changed while Treadwell was active; the conflicting value was left untouched.");
                    }
                    break;
                case StationOverrideApplyResult.NotExactPavedRoad:
                    if (reportNearMiss && IsNearPavedRoadPiece(piece)) LogCandidateMissingOnce();
                    break;
            }
        }

        private Piece FindPavedRoadPiece(PieceTable table, out bool nearMatchFound)
        {
            nearMatchFound = false;
            if (table.m_pieces == null) return null;
            foreach (var pieceObject in table.m_pieces)
            {
                if (pieceObject == null) continue;
                var piece = pieceObject.GetComponent<Piece>();
                if (_stationOverride.IsExactPavedRoad(piece)) return piece;
                if (IsNearPavedRoadPiece(piece)) nearMatchFound = true;
            }
            return null;
        }

        private static bool IsNearPavedRoadPiece(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return false;
            var prefabName = PavedRoadStationOverride<Piece, CraftingStation>.NormalizePrefabName(piece.gameObject.name);
            return string.Equals(prefabName, PavedRoadStationOverride<Piece, CraftingStation>.VanillaPrefabName, StringComparison.Ordinal) ||
                   string.Equals(piece.m_name, PavedRoadStationOverride<Piece, CraftingStation>.VanillaDisplayName, StringComparison.Ordinal);
        }

        private static bool IsLikelyHoePieceTable(PieceTable table)
        {
            var name = table != null && table.gameObject != null ? table.gameObject.name : null;
            return name != null && name.IndexOf("hoe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogCandidateMissingOnce()
        {
            if (_loggedCandidateMissing) return;
            _loggedCandidateMissing = true;
            Log.LogWarning("No exact Paved Road candidate was found in the active hoe piece table; the vanilla stonecutter requirement remains unchanged.");
        }

        private void RestorePavedRoadStation()
        {
            if (!_stationOverride.IsApplied) return;
            try
            {
                if (_stationOverride.Restore() == StationOverrideRestoreResult.Conflict)
                    Log.LogWarning("Did not restore the Paved Road station because another runtime change replaced it.");
            }
            catch (MissingReferenceException)
            {
                _stationOverride.Forget();
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
