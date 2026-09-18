#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
        private readonly string _harmonyId;
        private Harmony _harmony;
        private bool _active;
        private bool _cleanupPending;

        protected FeatureModuleBase(string id, ConfigEntry<bool> enabled, ManualLogSource log)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Feature id is required.", nameof(id));
            Id = id;
            Enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            Log = log ?? throw new ArgumentNullException(nameof(log));
            _harmonyId = Plugin.PluginGuid + ".feature." + id;
        }

        public string Id { get; }
        public ConfigEntry<bool> Enabled { get; }
        protected ManualLogSource Log { get; }
        protected Harmony Harmony => _harmony ?? throw new InvalidOperationException("Harmony was not initialized after compatibility validation.");

        public abstract void ValidateCompatibility(ICollection<string> failures);

        public void Enable()
        {
            if (_active) return;
            if (_cleanupPending) Disable();

            _cleanupPending = true;
            try
            {
                // Harmony construction is deliberately deferred until the complete
                // runtime contract has passed in Plugin.Awake.
                _harmony = new Harmony(_harmonyId);
                TransactionalInstall.Run(
                    InstallPatches,
                    () => _harmony?.UnpatchSelf(),
                    OnDisabled);
                _active = true;
                _cleanupPending = false;
                Log.LogInfo("Enabled feature module: " + Id);
            }
            catch
            {
                _active = false;
                // TransactionalInstall already attempted every cleanup action. Keep this
                // conservative marker until FeatureHost.Stop verifies cleanup end-to-end.
                throw;
            }
        }

        public void Disable()
        {
            if (!_active && !_cleanupPending) return;

            var failures = new List<Exception>();
            try { _harmony?.UnpatchSelf(); }
            catch (Exception exception) { failures.Add(exception); }

            try { OnDisabled(); }
            catch (Exception exception) { failures.Add(exception); }

            _active = false;
            if (failures.Count > 0)
            {
                _cleanupPending = true;
                throw new AggregateException("Feature cleanup failed for " + Id + ".", failures);
            }

            _harmony = null;
            _cleanupPending = false;
            Log.LogInfo("Disabled feature module: " + Id);
        }

        protected abstract void InstallPatches();
        protected virtual void OnDisabled() { }
    }

    internal sealed class RoadFeatureModule : FeatureModuleBase
    {
        internal const double NaturalGapHoldSeconds = 0.18d;
        private static RoadFeatureModule _activeModule;

        private static MethodInfo GetLastGroundColliderMethod;
        private static MethodInfo UpdateAvailablePiecesListMethod;

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
        private bool _loggedDiscoveryFailure;
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
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_resources", typeof(Piece.Requirement[]),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Piece.Requirement), "m_resItem", typeof(ItemDrop),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Piece.Requirement), "m_amount", typeof(int),
                BindingFlags.Instance | BindingFlags.Public);
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

            // Validate Harmony patch methods as exact pairs with the targets above before any patch is installed.
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PlayerSetPlaceModePrefix),
                new[] { typeof(PieceTable) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PieceTableUpdateAvailablePrefix),
                new[] { typeof(PieceTable) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PlayerHaveRequirementsPrefix),
                new[] { typeof(Piece) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(ZNetSceneOnDestroyPrefix),
                Type.EmptyTypes);
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(GetRunSpeedFactorPostfix),
                new[] { typeof(Player), typeof(float).MakeByRefType() });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(ModifyRunStaminaDrainPostfix),
                new[] { typeof(Character), typeof(float).MakeByRefType() });

            // These Unity contracts are used directly by road discovery and terrain sampling.
            CompatibilityGate.RequireGenericMethod(failures, typeof(GameObject), "GetComponent",
                BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes, returnsArray: false);
            CompatibilityGate.RequireGenericMethod(failures, typeof(GameObject), "GetComponentsInChildren",
                BindingFlags.Instance | BindingFlags.Public, new[] { typeof(bool) }, returnsArray: true);
            CompatibilityGate.RequireGenericMethod(failures, typeof(Component), "GetComponent",
                BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes, returnsArray: false);
            CompatibilityGate.RequireGenericMethod(failures, typeof(Component), "GetComponentInParent",
                BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes, returnsArray: false);
            CompatibilityGate.RequireProperty(failures, typeof(Component), "gameObject", typeof(GameObject),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Component), "transform", typeof(Transform),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Transform), "position", typeof(Vector3),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Time), "unscaledTime", typeof(float),
                BindingFlags.Static | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(UnityEngine.Object), "name", typeof(string),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireField(failures, typeof(Color), "r", typeof(float), BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Color), "g", typeof(float), BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Color), "b", typeof(float), BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(Color), "a", typeof(float), BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireMethod(failures, typeof(UnityEngine.Object), "op_Equality", typeof(bool),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) }, method => method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(UnityEngine.Object), "op_Inequality", typeof(bool),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(UnityEngine.Object), typeof(UnityEngine.Object) }, method => method.IsStatic);

            // Harmony itself is part of the runtime contract: validate exactly the API shapes emitted into this DLL.
            CompatibilityGate.RequireConstructor(failures, typeof(Harmony),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, new[] { typeof(string) });
            CompatibilityGate.RequireConstructor(failures, typeof(HarmonyMethod),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(Type), typeof(string), typeof(Type[]) });
            CompatibilityGate.RequireMethod(failures, typeof(Harmony), "Patch", typeof(MethodInfo),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(MethodBase), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod), typeof(HarmonyMethod) },
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Harmony), "UnpatchSelf", typeof(void),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(AccessTools), "DeclaredMethod", typeof(MethodInfo),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(Type), typeof(string), typeof(Type[]), typeof(Type[]) }, method => method.IsStatic);

            CompatibilityGate.RequireEnumValue(failures, typeof(TerrainModifier.PaintType), "Dirt", 0);
            CompatibilityGate.RequireEnumValue(failures, typeof(TerrainModifier.PaintType), "Cultivate", 1);
            CompatibilityGate.RequireEnumValue(failures, typeof(TerrainModifier.PaintType), "Paved", 2);

            CompatibilityGate.RequireColor(failures, "dirt paint", Heightmap.m_paintMaskDirt, 1f, 0f, 0f, 1f);
            CompatibilityGate.RequireColor(failures, "cultivated paint", Heightmap.m_paintMaskCultivated, 0f, 1f, 0f, 1f);
            CompatibilityGate.RequireColor(failures, "paved paint", Heightmap.m_paintMaskPaved, 0f, 0f, 1f, 1f);
        }

        protected override void InstallPatches()
        {
            if (_activeModule != null && !ReferenceEquals(_activeModule, this))
                throw new InvalidOperationException("Another road feature module is already active.");

            GetLastGroundColliderMethod = AccessTools.DeclaredMethod(
                typeof(Character), "GetLastGroundCollider", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Character).FullName, "GetLastGroundCollider");
            UpdateAvailablePiecesListMethod = AccessTools.DeclaredMethod(
                typeof(Player), "UpdateAvailablePiecesList", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Player).FullName, "UpdateAvailablePiecesList");
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
            var failures = new List<Exception>();
            try
            {
                if (_pavedSettingSubscribed)
                    _pavedRoadWithoutStonecutter.SettingChanged -= OnPavedRoadSettingChanged;
            }
            catch (Exception exception) { failures.Add(exception); }
            _pavedSettingSubscribed = false;

            try { RestorePavedRoadStation(); }
            catch (Exception exception) { failures.Add(exception); }

            try { RefreshPlayerAvailablePieces(); }
            catch (Exception exception) { Log.LogWarning("Could not refresh vanilla build-piece availability during cleanup: " + exception); }

            _lastPieceTable = null;
            if (ReferenceEquals(_activeModule, this)) _activeModule = null;
            GetLastGroundColliderMethod = null;
            UpdateAvailablePiecesListMethod = null;
            _surfaceTracker.Reset();

            if (failures.Count != 0)
                throw new AggregateException("Road feature cleanup was incomplete.", failures);
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
            // Re-discover from the active tool table rather than trusting an arbitrary Piece argument.
            _activeModule?.RefreshCurrentBuildPieces();
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

            var entries = InspectPieceTable(table);
            var shapes = new List<PavedRoadCandidateShape>(entries.Count);
            foreach (var entry in entries) shapes.Add(entry.Shape);
            var selection = PavedRoadCandidateSelector.Select(shapes);
            if (selection.Outcome != PavedRoadDiscoveryOutcome.Unique)
            {
                RestorePavedRoadStation();
                if (ReferenceEquals(_lastPieceTable, table)) _lastPieceTable = null;
                if (reportMissingCandidate && ShouldReportDiscoveryFailure(table, entries))
                    LogDiscoveryFailureOnce(table, selection, entries);
                return;
            }

            var selected = entries[selection.CandidateIndex];
            _lastPieceTable = table;
            ApplyPavedRoadStationOverride(selected.Piece, selected.Describe());
        }

        private void ApplyPavedRoadStationOverride(Piece piece, string candidateDescription)
        {
            if (!_pavedRoadWithoutStonecutter.Value || piece == null) return;

            var result = _stationOverride.Apply(piece);
            switch (result)
            {
                case StationOverrideApplyResult.Removed:
                    if (!_loggedStationRemoved)
                    {
                        _loggedStationRemoved = true;
                        Log.LogInfo("Semantic Paved Road candidate found " + candidateDescription +
                                    "; station field removed, so no nearby stonecutter is required.");
                    }
                    break;
                case StationOverrideApplyResult.AlreadyAbsent:
                    if (!_loggedStationAlreadyAbsent)
                    {
                        _loggedStationAlreadyAbsent = true;
                        Log.LogInfo("Semantic Paved Road candidate found " + candidateDescription +
                                    "; its station field was already absent, so no change was needed.");
                    }
                    break;
                case StationOverrideApplyResult.Conflict:
                    if (!_loggedStationConflict)
                    {
                        _loggedStationConflict = true;
                        Log.LogWarning("Paved Road station field changed while Treadwell was active; the conflicting value was left untouched.");
                    }
                    break;
                case StationOverrideApplyResult.InvalidPiece:
                    throw new InvalidOperationException("Semantic Paved Road discovery returned an invalid piece.");
            }
        }

        private List<PieceTableEntryInspection> InspectPieceTable(PieceTable table)
        {
            var entries = new List<PieceTableEntryInspection>();
            if (table.m_pieces == null) return entries;

            foreach (var pieceObject in table.m_pieces)
            {
                if (pieceObject == null) continue;
                var rootPiece = pieceObject.GetComponent<Piece>();
                var pieces = pieceObject.GetComponentsInChildren<Piece>(true);
                var modifiers = pieceObject.GetComponentsInChildren<TerrainModifier>(true);
                var piece = pieces.Length == 1 && ReferenceEquals(pieces[0], rootPiece) ? rootPiece : null;
                var pavedModifierCount = 0;
                foreach (var modifier in modifiers)
                {
                    if (modifier != null && modifier.m_paintType == TerrainModifier.PaintType.Paved)
                        pavedModifierCount++;
                }

                var requirements = piece != null ? piece.m_resources : null;
                var resourceCount = requirements != null ? requirements.Length : 0;
                var singleUnitResourceCount = 0;
                var singleUnitStoneResourceCount = 0;
                if (requirements != null)
                {
                    foreach (var requirement in requirements)
                    {
                        if (requirement == null || requirement.m_resItem == null || requirement.m_amount != 1) continue;
                        singleUnitResourceCount++;
                        if (IsExpectedStoneResource(requirement.m_resItem)) singleUnitStoneResourceCount++;
                    }
                }

                var shape = new PavedRoadCandidateShape(
                    pieces.Length,
                    piece != null,
                    modifiers.Length,
                    pavedModifierCount,
                    piece != null && (piece.m_craftingStation != null || _stationOverride.IsAppliedTo(piece)),
                    resourceCount,
                    singleUnitResourceCount,
                    singleUnitStoneResourceCount);
                entries.Add(new PieceTableEntryInspection(pieceObject, pieces, modifiers, shape));
            }
            return entries;
        }

        private static bool IsExpectedStoneResource(ItemDrop resource)
        {
            if (resource == null || resource.gameObject == null) return false;
            var name = resource.gameObject.name;
            return string.Equals(name, "Stone", StringComparison.Ordinal) ||
                   string.Equals(name, "Stone(Clone)", StringComparison.Ordinal);
        }

        private static bool ShouldReportDiscoveryFailure(PieceTable table, List<PieceTableEntryInspection> entries)
        {
            if (IsLikelyHoePieceTable(table)) return true;
            foreach (var entry in entries)
            {
                if (entry.Shape.PavedTerrainModifierCount > 0) return true;
            }
            return false;
        }

        private static bool IsLikelyHoePieceTable(PieceTable table)
        {
            var name = table != null && table.gameObject != null ? table.gameObject.name : null;
            return name != null && name.IndexOf("hoe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogDiscoveryFailureOnce(
            PieceTable table,
            PavedRoadCandidateSelection selection,
            List<PieceTableEntryInspection> entries)
        {
            if (_loggedDiscoveryFailure) return;
            _loggedDiscoveryFailure = true;
            var outcome = selection.Outcome == PavedRoadDiscoveryOutcome.Ambiguous
                ? "ambiguous (" + selection.CandidateCount + " semantic matches)"
                : "no semantic match";
            var tableName = table != null && table.gameObject != null ? SanitizeDiagnostic(table.gameObject.name) : "<unnamed>";
            var text = new StringBuilder();
            var shown = Math.Min(entries.Count, 12);
            for (var index = 0; index < shown; index++)
            {
                if (index > 0) text.Append("; ");
                text.Append(entries[index].Describe());
            }
            if (entries.Count > shown) text.Append("; +").Append(entries.Count - shown).Append(" more");
            Log.LogWarning("Paved Road semantic discovery was " + outcome + " in active table '" + tableName +
                           "'; vanilla station requirements remain unchanged. Candidate shapes: " + text);
        }

        private static string SanitizeDiagnostic(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<none>";
            var safe = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return safe.Length <= 64 ? safe : safe.Substring(0, 64) + "...";
        }

        private sealed class PieceTableEntryInspection
        {
            internal PieceTableEntryInspection(
                GameObject root,
                Piece[] pieces,
                TerrainModifier[] modifiers,
                PavedRoadCandidateShape shape)
            {
                Root = root;
                Pieces = pieces;
                Modifiers = modifiers;
                Shape = shape;
            }

            internal GameObject Root { get; }
            internal Piece[] Pieces { get; }
            internal TerrainModifier[] Modifiers { get; }
            internal PavedRoadCandidateShape Shape { get; }
            internal Piece Piece => Pieces.Length == 1 ? Pieces[0] : null;

            internal string Describe()
            {
                var piece = Piece;
                var text = new StringBuilder("[root='");
                text.Append(SanitizeDiagnostic(Root != null ? Root.name : null));
                text.Append("', pieces=").Append(Pieces.Length);
                text.Append(", piece='").Append(SanitizeDiagnostic(piece != null ? piece.m_name : null)).Append("'");
                text.Append(", station='");
                text.Append(SanitizeDiagnostic(piece != null && piece.m_craftingStation != null && piece.m_craftingStation.gameObject != null
                    ? piece.m_craftingStation.gameObject.name
                    : null));
                text.Append("', terrain=");
                AppendTerrainSummary(text, Modifiers);
                text.Append(", resources=");
                AppendResourceSummary(text, piece != null ? piece.m_resources : null);
                text.Append(']');
                return text.ToString();
            }

            private static void AppendTerrainSummary(StringBuilder text, TerrainModifier[] modifiers)
            {
                text.Append('[');
                var shown = Math.Min(modifiers.Length, 4);
                for (var index = 0; index < shown; index++)
                {
                    if (index > 0) text.Append(',');
                    text.Append(modifiers[index] != null ? modifiers[index].m_paintType.ToString() : "<null>");
                }
                if (modifiers.Length > shown) text.Append(",+").Append(modifiers.Length - shown);
                text.Append(']');
            }

            private static void AppendResourceSummary(StringBuilder text, Piece.Requirement[] requirements)
            {
                text.Append('[');
                if (requirements != null)
                {
                    var shown = Math.Min(requirements.Length, 4);
                    for (var index = 0; index < shown; index++)
                    {
                        if (index > 0) text.Append(',');
                        var requirement = requirements[index];
                        var itemName = requirement != null && requirement.m_resItem != null && requirement.m_resItem.gameObject != null
                            ? requirement.m_resItem.gameObject.name
                            : null;
                        text.Append(SanitizeDiagnostic(itemName)).Append(':')
                            .Append(requirement != null ? requirement.m_amount : 0);
                    }
                    if (requirements.Length > shown) text.Append(",+").Append(requirements.Length - shown);
                }
                text.Append(']');
            }
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
