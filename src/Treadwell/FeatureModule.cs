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
        private readonly Dictionary<Piece, TerrainBrushBinding> _terrainBrushes = new Dictionary<Piece, TerrainBrushBinding>();
        private readonly Dictionary<TerrainBrushKind, TerrainRadiusSelection> _terrainRadii =
            new Dictionary<TerrainBrushKind, TerrainRadiusSelection>
            {
                [TerrainBrushKind.LevelGround] = new TerrainRadiusSelection(TerrainBrushKind.LevelGround, 3f),
                [TerrainBrushKind.Pathen] = new TerrainRadiusSelection(TerrainBrushKind.Pathen, 2f),
                [TerrainBrushKind.PavedRoad] = new TerrainRadiusSelection(TerrainBrushKind.PavedRoad, 3f)
            };
        private PieceTable _radiusPieceTable;
        private int _radiusValidationFrame = -1;
        private bool _radiusValidationResult;
        private GameObject _scaledPlacementGhost;
        private readonly List<ScaledIndicatorTransform> _scaledIndicatorTransforms = new List<ScaledIndicatorTransform>();
        private static int _suppressCameraMouseWheelDepth;
        private PieceTable _lastPieceTable;
        private bool _pavedSettingSubscribed;
        private bool _loggedStationRemoved;
        private bool _loggedStationAlreadyAbsent;
        private bool _loggedDiscoveryFailure;
        private bool _loggedStationConflict;
        private bool _loggedRadiusDiscoveryFailure;

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
            CompatibilityGate.RequireMethod(failures, typeof(Player), "UpdatePlacement", typeof(void),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                new[] { typeof(bool), typeof(float) }, method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(GameCamera), "UpdateCamera", typeof(void),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                new[] { typeof(float) }, method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "PlacePiece", typeof(void),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) }, method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "InPlaceMode", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes, method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Player), "IsDead", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes, method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(PieceTable), "GetSelectedPiece", typeof(Piece),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireField(failures, typeof(Player), "m_placementGhost", typeof(GameObject),
                BindingFlags.Instance | BindingFlags.NonPublic);
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
            CompatibilityGate.RequireField(failures, typeof(Piece), "m_canRotate", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp), "m_settings", typeof(TerrainOp.Settings),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_level", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_levelRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_raise", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_raiseRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_smooth", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_smoothRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_paintCleared", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_paintType", typeof(TerrainModifier.PaintType),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireField(failures, typeof(TerrainOp.Settings), "m_paintRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public);
            CompatibilityGate.RequireMethod(failures, typeof(TerrainOp), "GetRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(TerrainOp.Settings), "GetRadius", typeof(float),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes,
                method => !method.IsStatic);
            CompatibilityGate.RequireProperty(failures, typeof(ObjectDB), "instance", typeof(ObjectDB),
                BindingFlags.Static | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireMethod(failures, typeof(ObjectDB), "TryGetTerrainOp", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(string), typeof(TerrainOp).MakeByRefType() }, method => !method.IsStatic);
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
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PlayerUpdatePlacementPrefix),
                new[] { typeof(Player), typeof(bool), typeof(GameObject) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PlayerPlacePiecePrefix),
                new[] { typeof(Player), typeof(Piece), typeof(RadiusMutation).MakeByRefType() });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(PlayerPlacePieceFinalizer),
                typeof(Exception), new[] { typeof(Exception), typeof(RadiusMutation) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(GameCameraUpdateCameraPrefix),
                new[] { typeof(bool).MakeByRefType() });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(GameCameraUpdateCameraFinalizer),
                typeof(Exception), new[] { typeof(Exception), typeof(bool) });
            CompatibilityGate.RequirePatchMethod(failures, typeof(RoadFeatureModule), nameof(ZInputGetMouseScrollWheelPrefix),
                typeof(bool), new[] { typeof(float).MakeByRefType() });
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
            CompatibilityGate.RequireProperty(failures, typeof(GameObject), "activeInHierarchy", typeof(bool),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Component), "transform", typeof(Transform),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Transform), "position", typeof(Vector3),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(Transform), "localScale", typeof(Vector3),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: true);
            CompatibilityGate.RequireProperty(failures, typeof(ParticleSystem), "main", typeof(ParticleSystem.MainModule),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireProperty(failures, typeof(ParticleSystem.MainModule), "scalingMode", typeof(ParticleSystemScalingMode),
                BindingFlags.Instance | BindingFlags.Public, requireGetter: true, requireSetter: true);
            CompatibilityGate.RequireMethod(failures, typeof(Transform), "Find", typeof(Transform),
                BindingFlags.Instance | BindingFlags.Public, new[] { typeof(string) }, method => !method.IsStatic);
            CompatibilityGate.RequireProperty(failures, typeof(Time), "frameCount", typeof(int),
                BindingFlags.Static | BindingFlags.Public, requireGetter: true, requireSetter: false);
            CompatibilityGate.RequireMethod(failures, typeof(ZInput), "GetKey", typeof(bool),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
                new[] { typeof(KeyCode), typeof(bool) }, method => method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(ZInput), "GetMouseScrollWheel", typeof(float),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes, method => method.IsStatic);
            CompatibilityGate.RequireMethod(failures, typeof(Hud), "IsPieceSelectionVisible", typeof(bool),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly, Type.EmptyTypes, method => method.IsStatic);
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
            CompatibilityGate.RequireEnumValue(failures, typeof(ParticleSystemScalingMode), "Local", 1);

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
                AccessTools.DeclaredMethod(typeof(Player), "UpdatePlacement", new[] { typeof(bool), typeof(float) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PlayerUpdatePlacementPrefix)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(Player), "PlacePiece",
                    new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PlayerPlacePiecePrefix)),
                finalizer: new HarmonyMethod(typeof(RoadFeatureModule), nameof(PlayerPlacePieceFinalizer)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(GameCamera), "UpdateCamera", new[] { typeof(float) }),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(GameCameraUpdateCameraPrefix)),
                finalizer: new HarmonyMethod(typeof(RoadFeatureModule), nameof(GameCameraUpdateCameraFinalizer)));
            Harmony.Patch(
                AccessTools.DeclaredMethod(typeof(ZInput), "GetMouseScrollWheel", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(RoadFeatureModule), nameof(ZInputGetMouseScrollWheelPrefix)));
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
            if (_pavedSettingSubscribed)
            {
                try
                {
                    _pavedRoadWithoutStonecutter.SettingChanged -= OnPavedRoadSettingChanged;
                    _pavedSettingSubscribed = false;
                }
                catch (Exception exception) { failures.Add(exception); }
            }

            try { RestorePavedRoadStation(); }
            catch (Exception exception) { failures.Add(exception); }
            try { ResetTerrainRadiusRuntime(); }
            catch (Exception exception) { failures.Add(exception); }

            // Clear every gameplay entrypoint before best-effort UI refresh/logging.
            _lastPieceTable = null;
            if (ReferenceEquals(_activeModule, this)) _activeModule = null;
            _surfaceTracker.Reset();

            try { RefreshPlayerAvailablePieces(); }
            catch (Exception exception)
            {
                try { Log.LogWarning("Could not refresh vanilla build-piece availability during cleanup: " + exception); }
                catch { }
            }
            GetLastGroundColliderMethod = null;
            UpdateAvailablePiecesListMethod = null;

            if (failures.Count != 0)
                throw new AggregateException("Road feature cleanup was incomplete.", failures);
        }

        private static void PlayerSetPlaceModePrefix(PieceTable __0)
        {
            var module = _activeModule;
            if (module == null) return;
            module.RefreshTerrainRadiusBrushes(__0);
            module.RefreshPavedRoadStation(__0, reportMissingCandidate: true);
        }

        private static void PieceTableUpdateAvailablePrefix(PieceTable __instance)
        {
            var module = _activeModule;
            if (module == null) return;
            var player = Player.m_localPlayer;
            if (player != null && ReferenceEquals(player.GetBuildTool(), __instance))
                module.RefreshTerrainRadiusBrushes(__instance);
            module.RefreshPavedRoadStation(__instance, reportMissingCandidate: false);
        }

        private static void PlayerHaveRequirementsPrefix(Piece __0)
        {
            // Re-discover from the active tool table rather than trusting an arbitrary Piece argument.
            _activeModule?.RefreshCurrentBuildPieces();
        }

        private static void PlayerUpdatePlacementPrefix(Player __instance, bool __0, GameObject ___m_placementGhost)
        {
            var module = _activeModule;
            if (module == null) return;
            try { module.UpdateTerrainRadiusInput(__instance, __0, ___m_placementGhost); }
            catch (Exception exception)
            {
                module.ResetTerrainRadiusRuntime();
                module.Log.LogError("Terrain-radius input failed and its runtime state was reset: " + exception);
            }
        }

        private static void PlayerPlacePiecePrefix(Player __instance, Piece __0, ref RadiusMutation __state)
        {
            var module = _activeModule;
            if (module == null) return;
            try { __state = module.BeginRadiusMutation(__instance, __0); }
            catch (Exception exception)
            {
                __state?.Restore(module.Log);
                __state = null;
                module.Log.LogError("Terrain-radius placement override failed before placement; vanilla radii were retained: " + exception);
            }
        }

        private static Exception PlayerPlacePieceFinalizer(Exception __exception, RadiusMutation __state)
        {
            var module = _activeModule;
            try { __state?.Restore(module?.Log); }
            catch (Exception exception)
            {
                try { module?.Log.LogError("Terrain-radius placement cleanup failed: " + exception); }
                catch { }
            }
            return __exception;
        }

        private static void GameCameraUpdateCameraPrefix(ref bool __state)
        {
            __state = false;
            var module = _activeModule;
            if (module == null) return;
            try
            {
                if (!module.ShouldSuppressCameraZoom()) return;
                _suppressCameraMouseWheelDepth++;
                __state = true;
            }
            catch (Exception exception)
            {
                module.Log.LogError("Camera-wheel routing failed closed to vanilla zoom: " + exception);
            }
        }

        private static Exception GameCameraUpdateCameraFinalizer(Exception __exception, bool __state)
        {
            if (__state && _suppressCameraMouseWheelDepth > 0) _suppressCameraMouseWheelDepth--;
            return __exception;
        }

        private static bool ZInputGetMouseScrollWheelPrefix(ref float __result)
        {
            if (_activeModule == null || _suppressCameraMouseWheelDepth <= 0) return true;
            __result = 0f;
            return false;
        }

        private bool ShouldSuppressCameraZoom()
        {
            var player = Player.m_localPlayer;
            var table = player != null ? player.GetBuildTool() : null;
            var altHeld = ZInput.GetKey(KeyCode.LeftAlt, false) || ZInput.GetKey(KeyCode.RightAlt, false);
            return CameraWheelRouting.ShouldSuppressZoom(
                player != null,
                player != null && player.InPlaceMode(),
                player != null && player.IsDead(),
                IsLikelyHoePieceTable(table),
                altHeld,
                Hud.IsPieceSelectionVisible());
        }

        private static void ZNetSceneOnDestroyPrefix()
        {
            var module = _activeModule;
            if (module == null) return;
            module.RestorePavedRoadStation();
            module.ResetTerrainRadiusRuntime();
            module._lastPieceTable = null;
        }

        private void RefreshTerrainRadiusBrushes(PieceTable table)
        {
            RestoreScaledPlacementGhost();
            _terrainBrushes.Clear();
            _radiusPieceTable = table;
            _radiusValidationFrame = -1;
            _radiusValidationResult = false;
            if (table == null) return;

            var candidates = new Dictionary<TerrainBrushKind, List<PieceTableEntryInspection>>
            {
                [TerrainBrushKind.LevelGround] = new List<PieceTableEntryInspection>(),
                [TerrainBrushKind.Pathen] = new List<PieceTableEntryInspection>(),
                [TerrainBrushKind.PavedRoad] = new List<PieceTableEntryInspection>()
            };
            foreach (var entry in InspectPieceTable(table))
            {
                var kind = VanillaTerrainBrushClassifier.Classify(entry.BrushShape);
                if (kind == TerrainBrushKind.None || entry.TerrainOps.Length != 1 || entry.TerrainOps[0] == null) continue;
                if (!HasExpectedVanillaRadii(kind, entry.TerrainOps[0].m_settings)) continue;
                candidates[kind].Add(entry);
            }

            if (candidates[TerrainBrushKind.LevelGround].Count != 1 ||
                candidates[TerrainBrushKind.Pathen].Count != 1 ||
                candidates[TerrainBrushKind.PavedRoad].Count != 1)
            {
                if (!_loggedRadiusDiscoveryFailure)
                {
                    _loggedRadiusDiscoveryFailure = true;
                    Log.LogWarning("Terrain-radius controls were left disabled for this piece table because exact vanilla brush discovery was not unique " +
                                   "(Level Ground=" + candidates[TerrainBrushKind.LevelGround].Count +
                                   ", Pathen=" + candidates[TerrainBrushKind.Pathen].Count +
                                   ", Paved Road=" + candidates[TerrainBrushKind.PavedRoad].Count + ").");
                }
                return;
            }

            foreach (var pair in candidates)
            {
                var entry = pair.Value[0];
                var terrainOp = entry.TerrainOps[0];
                _terrainBrushes.Add(entry.Piece,
                    new TerrainBrushBinding(pair.Key, entry.Piece, terrainOp, terrainOp.GetRadius()));
            }
        }

        private static bool HasExpectedVanillaRadii(TerrainBrushKind kind, TerrainOp.Settings settings)
        {
            if (settings == null) return false;
            switch (kind)
            {
                case TerrainBrushKind.LevelGround:
                    return !settings.m_level && !settings.m_raise && settings.m_smooth && settings.m_paintCleared &&
                           settings.m_paintType == TerrainModifier.PaintType.Dirt &&
                           Approximately(settings.GetRadius(), 3f) &&
                           Approximately(settings.m_smoothRadius, 3f) && Approximately(settings.m_paintRadius, 3f);
                case TerrainBrushKind.Pathen:
                    return !settings.m_level && !settings.m_raise && !settings.m_smooth && settings.m_paintCleared &&
                           settings.m_paintType == TerrainModifier.PaintType.Dirt &&
                           Approximately(settings.GetRadius(), 2f) && Approximately(settings.m_paintRadius, 2f);
                case TerrainBrushKind.PavedRoad:
                    return !settings.m_level && !settings.m_raise && settings.m_smooth && settings.m_paintCleared &&
                           settings.m_paintType == TerrainModifier.PaintType.Paved &&
                           Approximately(settings.GetRadius(), 3f) &&
                           Approximately(settings.m_smoothRadius, 3f) && Approximately(settings.m_paintRadius, 2.2f);
                default:
                    return false;
            }
        }

        private bool RevalidateTerrainRadiusBindings(bool force)
        {
            var frame = Time.frameCount;
            if (!force && _radiusValidationFrame == frame) return _radiusValidationResult;

            _radiusValidationFrame = frame;
            _radiusValidationResult = false;
            if (_radiusPieceTable == null || _terrainBrushes.Count != 3) return false;

            var candidates = new Dictionary<TerrainBrushKind, List<PieceTableEntryInspection>>
            {
                [TerrainBrushKind.LevelGround] = new List<PieceTableEntryInspection>(),
                [TerrainBrushKind.Pathen] = new List<PieceTableEntryInspection>(),
                [TerrainBrushKind.PavedRoad] = new List<PieceTableEntryInspection>()
            };
            foreach (var entry in InspectPieceTable(_radiusPieceTable))
            {
                var kind = VanillaTerrainBrushClassifier.Classify(entry.BrushShape);
                if (kind == TerrainBrushKind.None || entry.TerrainOps.Length != 1 || entry.TerrainOps[0] == null) continue;
                if (!HasExpectedVanillaRadii(kind, entry.TerrainOps[0].m_settings)) continue;
                candidates[kind].Add(entry);
            }

            foreach (var pair in candidates)
            {
                if (pair.Value.Count != 1) return false;
                var entry = pair.Value[0];
                if (entry.Piece == null || !_terrainBrushes.TryGetValue(entry.Piece, out var binding) ||
                    binding.Kind != pair.Key || !ReferenceEquals(binding.Piece, entry.Piece) ||
                    !ReferenceEquals(binding.TerrainOp, entry.TerrainOps[0]) ||
                    !Approximately(binding.VanillaRadius, entry.TerrainOps[0].GetRadius()))
                {
                    return false;
                }
            }

            _radiusValidationResult = true;
            return true;
        }

        private void UpdateTerrainRadiusInput(Player player, bool takeInput, GameObject placementGhost)
        {
            if (player == null || player != Player.m_localPlayer || !player.InPlaceMode() || player.IsDead() ||
                !takeInput || Hud.IsPieceSelectionVisible())
            {
                RestoreScaledPlacementGhost();
                return;
            }

            var table = player.GetBuildTool();
            if (!ReferenceEquals(table, _radiusPieceTable)) RefreshTerrainRadiusBrushes(table);
            var selected = table != null ? table.GetSelectedPiece() : null;
            if (selected == null || !_terrainBrushes.TryGetValue(selected, out var binding))
            {
                RestoreScaledPlacementGhost();
                return;
            }

            if (!SynchronizePlacementGhost(placementGhost, binding)) return;
            if (!ZInput.GetKey(KeyCode.LeftAlt, false) && !ZInput.GetKey(KeyCode.RightAlt, false)) return;

            var wheel = ZInput.GetMouseScrollWheel();
            if (wheel == 0f) return;

            var current = _terrainRadii[binding.Kind];
            var next = current.Scroll(wheel);
            if (Approximately(next.Radius, current.Radius)) return;
            _terrainRadii[binding.Kind] = next;
            SynchronizePlacementGhost(placementGhost, binding);
        }

        private bool SynchronizePlacementGhost(GameObject placementGhost, TerrainBrushBinding binding)
        {
            if (binding == null || binding.TerrainOp == null ||
                !RevalidateTerrainRadiusBindings(force: false) ||
                !HasExpectedVanillaRadii(binding.Kind, binding.TerrainOp.m_settings))
            {
                RestoreScaledPlacementGhost();
                return false;
            }
            if (placementGhost == null)
            {
                RestoreScaledPlacementGhost();
                return false;
            }

            var marker = placementGhost.transform.Find("_GhostOnly");
            if (marker == null)
            {
                RestoreScaledPlacementGhost();
                return false;
            }

            var markerObject = marker.gameObject;
            if (!TerrainIndicatorRouting.CanSynchronize(markerObject != null, markerObject.activeInHierarchy))
            {
                RestoreScaledPlacementGhost();
                return false;
            }
            if (!ReferenceEquals(_scaledPlacementGhost, markerObject))
            {
                RestoreScaledPlacementGhost();
                _scaledPlacementGhost = markerObject;
                _scaledIndicatorTransforms.Add(new ScaledIndicatorTransform(marker));
                foreach (var particle in markerObject.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (particle == null || ReferenceEquals(particle.transform, marker) ||
                        !TerrainIndicatorRouting.ShouldScaleOwnTransform(
                            isGhostOnlyMarker: false,
                            isLocalScalingParticle: particle.main.scalingMode == ParticleSystemScalingMode.Local))
                    {
                        continue;
                    }
                    _scaledIndicatorTransforms.Add(new ScaledIndicatorTransform(particle.transform));
                }
            }

            var radius = _terrainRadii[binding.Kind];
            var scale = radius.ScaleFor(binding.VanillaRadius);
            foreach (var target in _scaledIndicatorTransforms) target.Apply(scale);
            binding.IndicatorSynchronized = true;
            return true;
        }

        private RadiusMutation BeginRadiusMutation(Player player, Piece piece)
        {
            if (player == null || player != Player.m_localPlayer || !player.InPlaceMode() || player.IsDead() || piece == null)
                return null;
            var table = player.GetBuildTool();
            if (!ReferenceEquals(table, _radiusPieceTable)) RefreshTerrainRadiusBrushes(table);
            if (table == null || !ReferenceEquals(table.GetSelectedPiece(), piece)) return null;
            if (!_terrainBrushes.TryGetValue(piece, out var binding) || !binding.IndicatorSynchronized ||
                binding.TerrainOp == null)
            {
                return null;
            }
            if (!RevalidateTerrainRadiusBindings(force: true))
            {
                RestoreScaledPlacementGhost();
                return null;
            }
            if (!HasExpectedVanillaRadii(binding.Kind, binding.TerrainOp.m_settings) ||
                _scaledPlacementGhost == null || !_scaledPlacementGhost.activeInHierarchy ||
                !AreIndicatorTransformsApplied())
            {
                return null;
            }

            var radius = _terrainRadii[binding.Kind];
            if (Approximately(radius.Radius, binding.VanillaRadius)) return null;

            // TerrainOp.Awake uses the cloned table prefab's values to find affected
            // heightmaps, but TerrainComp's RPC resolves the operation settings again
            // through ObjectDB by prefab name. Keep both authoritative sources changed
            // for the synchronous placement call, then restore both in the finalizer.
            var objectDb = ObjectDB.instance;
            TerrainOp registeredTerrainOp;
            if (objectDb == null || piece.gameObject == null ||
                !objectDb.TryGetTerrainOp(piece.gameObject.name, out registeredTerrainOp) ||
                registeredTerrainOp == null ||
                !HasExpectedVanillaRadii(binding.Kind, registeredTerrainOp.m_settings))
            {
                return null;
            }

            var mutation = new RadiusMutation(binding.TerrainOp, registeredTerrainOp);
            mutation.Apply(radius.Radius);
            return mutation;
        }

        private void ResetTerrainRadiusRuntime()
        {
            RestoreScaledPlacementGhost();
            foreach (var binding in _terrainBrushes.Values) binding.IndicatorSynchronized = false;
            _terrainBrushes.Clear();
            _radiusPieceTable = null;
            _radiusValidationFrame = -1;
            _radiusValidationResult = false;
            _suppressCameraMouseWheelDepth = 0;
        }

        private bool AreIndicatorTransformsApplied()
        {
            if (_scaledIndicatorTransforms.Count == 0) return false;
            foreach (var target in _scaledIndicatorTransforms)
            {
                if (!target.IsApplied()) return false;
            }
            return true;
        }

        private void RestoreScaledPlacementGhost()
        {
            foreach (var target in _scaledIndicatorTransforms) target.Restore();
            foreach (var binding in _terrainBrushes.Values) binding.IndicatorSynchronized = false;
            _scaledIndicatorTransforms.Clear();
            _scaledPlacementGhost = null;
        }

        private static bool Approximately(float left, float right)
            => !float.IsNaN(left) && !float.IsNaN(right) && Math.Abs(left - right) <= 0.0001f;

        private static bool Approximately(Vector3 left, Vector3 right)
            => Approximately(left.x, right.x) && Approximately(left.y, right.y) && Approximately(left.z, right.z);

        private void OnPavedRoadSettingChanged(object sender, EventArgs eventArgs)
        {
            if (!ReferenceEquals(_activeModule, this)) return;
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
                var rootTerrainOp = pieceObject.GetComponent<TerrainOp>();
                var pieces = pieceObject.GetComponentsInChildren<Piece>(true);
                var terrainOps = pieceObject.GetComponentsInChildren<TerrainOp>(true);
                var modifiers = pieceObject.GetComponentsInChildren<TerrainModifier>(true);
                var piece = pieces.Length == 1 && ReferenceEquals(pieces[0], rootPiece) ? rootPiece : null;
                var terrainOp = terrainOps.Length == 1 && ReferenceEquals(terrainOps[0], rootTerrainOp) ? rootTerrainOp : null;
                var settings = terrainOp != null ? terrainOp.m_settings : null;

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

                var hasStationRequirement = piece != null &&
                    (piece.m_craftingStation != null || _stationOverride.IsAppliedTo(piece));
                var brushShape = new TerrainBrushShape(
                    pieces.Length,
                    piece != null,
                    terrainOps.Length,
                    terrainOp != null,
                    modifiers.Length,
                    pieceObject.name,
                    piece != null ? piece.m_name : null,
                    piece != null && piece.m_canRotate,
                    settings != null && settings.m_level,
                    settings != null && settings.m_raise,
                    settings != null && settings.m_smooth,
                    settings != null && settings.m_paintCleared,
                    settings != null ? (int)settings.m_paintType : -1,
                    hasStationRequirement,
                    resourceCount,
                    singleUnitResourceCount,
                    singleUnitStoneResourceCount);

                var pavedOperationCount = 0;
                foreach (var modifier in modifiers)
                {
                    if (modifier != null && modifier.m_paintType == TerrainModifier.PaintType.Paved)
                        pavedOperationCount++;
                }
                foreach (var operation in terrainOps)
                {
                    if (operation != null && operation.m_settings != null && operation.m_settings.m_paintCleared &&
                        operation.m_settings.m_paintType == TerrainModifier.PaintType.Paved)
                        pavedOperationCount++;
                }
                // PavedRoadCandidateShape predates Valheim's TerrainOp migration; its
                // terrain count now deliberately represents both supported operation types.
                var stationTerrainCount = modifiers.Length + terrainOps.Length;
                var shape = new PavedRoadCandidateShape(
                    pieces.Length,
                    piece != null,
                    stationTerrainCount,
                    pavedOperationCount,
                    hasStationRequirement,
                    resourceCount,
                    singleUnitResourceCount,
                    singleUnitStoneResourceCount);
                entries.Add(new PieceTableEntryInspection(pieceObject, pieces, modifiers, terrainOps, shape, brushShape));
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
                if (entry.Shape.PavedTerrainOperationCount > 0) return true;
            }
            return false;
        }

        private static bool IsLikelyHoePieceTable(PieceTable table)
        {
            var name = table != null && table.gameObject != null ? table.gameObject.name : null;
            return string.Equals(name, "_HoePieceTable", StringComparison.Ordinal) ||
                   string.Equals(name, "_HoePieceTable(Clone)", StringComparison.Ordinal);
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

        private sealed class ScaledIndicatorTransform
        {
            private readonly Transform _transform;
            private readonly Vector3 _baseScale;
            private Vector3 _appliedScale;

            internal ScaledIndicatorTransform(Transform transform)
            {
                _transform = transform ?? throw new ArgumentNullException(nameof(transform));
                _baseScale = transform.localScale;
                _appliedScale = _baseScale;
            }

            internal void Apply(float factor)
            {
                var applied = new TerrainIndicatorScale(_baseScale.x, _baseScale.y, _baseScale.z)
                    .ScaleUniformly(factor);
                _appliedScale = new Vector3(applied.X, applied.Y, applied.Z);
                _transform.localScale = _appliedScale;
            }

            internal bool IsApplied()
                => _transform != null && Approximately(_transform.localScale, _appliedScale);

            internal void Restore()
            {
                if (_transform != null && Approximately(_transform.localScale, _appliedScale))
                    _transform.localScale = _baseScale;
            }
        }

        private sealed class TerrainBrushBinding
        {
            internal TerrainBrushBinding(TerrainBrushKind kind, Piece piece, TerrainOp terrainOp, float vanillaRadius)
            {
                Kind = kind;
                Piece = piece;
                TerrainOp = terrainOp;
                VanillaRadius = vanillaRadius;
            }

            internal TerrainBrushKind Kind { get; }
            internal Piece Piece { get; }
            internal TerrainOp TerrainOp { get; }
            internal float VanillaRadius { get; }
            internal bool IndicatorSynchronized { get; set; }
        }

        private sealed class RadiusMutation
        {
            private readonly TerrainRadiusMutationSession _session;

            internal RadiusMutation(params TerrainOp[] terrainOps)
            {
                if (terrainOps == null || terrainOps.Length == 0)
                    throw new ArgumentNullException(nameof(terrainOps));

                var targets = new List<ITerrainRadiusMutationTarget>();
                foreach (var terrainOp in terrainOps)
                {
                    if (terrainOp == null || terrainOp.m_settings == null)
                        throw new ArgumentNullException(nameof(terrainOps));
                    targets.Add(new TerrainOpSettingsTarget(terrainOp.m_settings));
                }
                _session = new TerrainRadiusMutationSession(targets);
            }

            internal void Apply(float targetRadius) => _session.Apply(targetRadius);

            internal void Restore(ManualLogSource log)
            {
                if (_session.Restore())
                    log?.LogWarning("A terrain-radius field changed during placement; Treadwell left that conflicting field untouched.");
            }
        }

        private sealed class TerrainOpSettingsTarget : ITerrainRadiusMutationTarget
        {
            private readonly TerrainOp.Settings _settings;

            internal TerrainOpSettingsTarget(TerrainOp.Settings settings)
            {
                _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            }

            public object Identity => _settings;
            public bool LevelActive => _settings.m_level;
            public bool RaiseActive => _settings.m_raise;
            public bool SmoothActive => _settings.m_smooth;
            public bool PaintActive => _settings.m_paintCleared;
            public float LevelRadius { get => _settings.m_levelRadius; set => _settings.m_levelRadius = value; }
            public float RaiseRadius { get => _settings.m_raiseRadius; set => _settings.m_raiseRadius = value; }
            public float SmoothRadius { get => _settings.m_smoothRadius; set => _settings.m_smoothRadius = value; }
            public float PaintRadius { get => _settings.m_paintRadius; set => _settings.m_paintRadius = value; }
        }

        private sealed class PieceTableEntryInspection
        {
            internal PieceTableEntryInspection(
                GameObject root,
                Piece[] pieces,
                TerrainModifier[] modifiers,
                TerrainOp[] terrainOps,
                PavedRoadCandidateShape shape,
                TerrainBrushShape brushShape)
            {
                Root = root;
                Pieces = pieces;
                Modifiers = modifiers;
                TerrainOps = terrainOps;
                Shape = shape;
                BrushShape = brushShape;
            }

            internal GameObject Root { get; }
            internal Piece[] Pieces { get; }
            internal TerrainModifier[] Modifiers { get; }
            internal TerrainOp[] TerrainOps { get; }
            internal PavedRoadCandidateShape Shape { get; }
            internal TerrainBrushShape BrushShape { get; }
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
                AppendTerrainSummary(text, Modifiers, TerrainOps);
                text.Append(", resources=");
                AppendResourceSummary(text, piece != null ? piece.m_resources : null);
                text.Append(']');
                return text.ToString();
            }

            private static void AppendTerrainSummary(
                StringBuilder text,
                TerrainModifier[] modifiers,
                TerrainOp[] terrainOps)
            {
                text.Append('[');
                var written = 0;
                foreach (var modifier in modifiers)
                {
                    if (written >= 4) break;
                    if (written++ > 0) text.Append(',');
                    text.Append("modifier:").Append(modifier != null ? modifier.m_paintType.ToString() : "<null>");
                }
                foreach (var operation in terrainOps)
                {
                    if (written >= 4) break;
                    if (written++ > 0) text.Append(',');
                    text.Append("op:").Append(operation != null && operation.m_settings != null
                        ? operation.m_settings.m_paintType.ToString()
                        : "<null>");
                }
                var remaining = modifiers.Length + terrainOps.Length - written;
                if (remaining > 0) text.Append(",+").Append(remaining);
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
