using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Treadwell.Core;

namespace Treadwell.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static int Main()
        {
            ClassificationTests();
            TuningTests();
            HysteresisTests();
            TerrainRadiusTests();
            RuntimeSafetyTests();
            Console.WriteLine(_passed + " core tests passed");
            return 0;
        }

        private static void ClassificationTests()
        {
            Run("unpainted terrain is natural", () => Equal(TerrainSurface.Natural, TerrainClassifier.Classify(0f, 0f, 0f)));
            Run("pure dirt paint is dirt path", () => Equal(TerrainSurface.DirtPath, TerrainClassifier.Classify(1f, 0f, 0f)));
            Run("pure paved paint is paved road", () => Equal(TerrainSurface.PavedRoad, TerrainClassifier.Classify(0f, 0f, 1f)));
            Run("pure cultivation paint is cultivated", () => Equal(TerrainSurface.Cultivated, TerrainClassifier.Classify(0f, 1f, 0f)));
            Run("strong red blend is dirt", () => Equal(TerrainSurface.DirtPath, TerrainClassifier.Classify(0.72f, 0.08f, 0.20f)));
            Run("strong blue blend is paved", () => Equal(TerrainSurface.PavedRoad, TerrainClassifier.Classify(0.18f, 0.04f, 0.78f)));
            Run("cultivation wins a close dirt boundary", () => Equal(TerrainSurface.Cultivated, TerrainClassifier.Classify(0.50f, 0.49f, 0.01f)));
            Run("road-paint tie is natural gap", () => Equal(TerrainSurface.Natural, TerrainClassifier.Classify(0.50f, 0f, 0.50f)));
            Run("weak paint noise is natural", () => Equal(TerrainSurface.Natural, TerrainClassifier.Classify(0.09f, 0f, 0f)));
            Run("non-finite paint is natural", () => Equal(TerrainSurface.Natural, TerrainClassifier.Classify(float.NaN, 0f, 1f)));
        }

        private static void TuningTests()
        {
            var defaults = new RoadTuning(10f, 10f, 20f, 20f);
            Run("natural speed is unchanged", () => Near(1f, defaults.SpeedMultiplier(RoadSurface.None)));
            Run("natural stamina is unchanged", () => Near(1f, defaults.StaminaMultiplier(RoadSurface.None)));
            Run("dirt speed default is plus ten percent", () => Near(1.10f, defaults.SpeedMultiplier(RoadSurface.Dirt)));
            Run("dirt stamina default is ten percent lower", () => Near(0.90f, defaults.StaminaMultiplier(RoadSurface.Dirt)));
            Run("paved speed default is plus twenty percent", () => Near(1.20f, defaults.SpeedMultiplier(RoadSurface.Paved)));
            Run("paved stamina default is twenty percent lower", () => Near(0.80f, defaults.StaminaMultiplier(RoadSurface.Paved)));
            Run("negative percentages clamp to zero", () => Near(0f, new RoadTuning(-5f, -1f, -10f, -2f).PavedSpeedPercent));
            Run("percentages above one hundred clamp", () => Near(0f, new RoadTuning(500f, 500f, 500f, 500f).StaminaMultiplier(RoadSurface.Paved)));
            Run("non-finite percentages clamp safely", () =>
            {
                var tuning = new RoadTuning(float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NaN);
                Near(1f, tuning.SpeedMultiplier(RoadSurface.Dirt));
                Near(0f, tuning.StaminaMultiplier(RoadSurface.Dirt));
                Near(1f, tuning.SpeedMultiplier(RoadSurface.Paved));
                Near(1f, tuning.StaminaMultiplier(RoadSurface.Paved));
            });
            Run("road multiplier preserves vanilla result", () => Near(1.375f, 1.25f * defaults.SpeedMultiplier(RoadSurface.Dirt)));
        }

        private static void HysteresisTests()
        {
            Run("dirt becomes active immediately", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                Equal(RoadSurface.Dirt, tracker.Observe(TerrainSurface.DirtPath, 1d));
            });
            Run("paved becomes active immediately", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                Equal(RoadSurface.Paved, tracker.Observe(TerrainSurface.PavedRoad, 1d));
            });
            Run("natural boundary gap briefly retains road", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.DirtPath, 1d);
                Equal(RoadSurface.Dirt, tracker.Observe(TerrainSurface.Natural, 1.17d));
            });
            Run("natural boundary gap expires promptly", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.PavedRoad, 2d);
                Equal(RoadSurface.None, tracker.Observe(TerrainSurface.Natural, 2.181d));
            });
            Run("fresh road sample renews the hold", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.DirtPath, 3d);
                tracker.Observe(TerrainSurface.Natural, 3.10d);
                tracker.Observe(TerrainSurface.DirtPath, 3.15d);
                Equal(RoadSurface.Dirt, tracker.Observe(TerrainSurface.Natural, 3.32d));
            });
            Run("road type switches immediately", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.DirtPath, 4d);
                Equal(RoadSurface.Paved, tracker.Observe(TerrainSurface.PavedRoad, 4.01d));
            });
            Run("cultivated soil clears immediately", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.DirtPath, 5d);
                Equal(RoadSurface.None, tracker.Observe(TerrainSurface.Cultivated, 5.01d));
            });
            Run("building floor clears immediately", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.PavedRoad, 6d);
                Equal(RoadSurface.None, tracker.Observe(TerrainSurface.NonTerrain, 6.01d));
            });
            Run("clock reversal resets fail closed", () =>
            {
                var tracker = new RoadSurfaceTracker(0.18d);
                tracker.Observe(TerrainSurface.DirtPath, 7d);
                Equal(RoadSurface.None, tracker.Observe(TerrainSurface.Natural, 6d));
            });
        }

        private static void TerrainRadiusTests()
        {
            TerrainBrushShape Shape(
                string prefab,
                string pieceName,
                bool level = false,
                bool raise = false,
                bool smooth = false,
                bool paint = false,
                int paintType = -1,
                bool station = false,
                int resources = 0,
                int single = 0,
                int stone = 0,
                int pieces = 1,
                bool rootPiece = true,
                int terrainOps = 1,
                bool rootTerrainOp = true,
                int terrainModifiers = 0,
                bool canRotate = false)
                => new TerrainBrushShape(pieces, rootPiece, terrainOps, rootTerrainOp, terrainModifiers,
                    prefab, pieceName, canRotate, level, raise, smooth, paint, paintType,
                    station, resources, single, stone);

            TerrainBrushShape LevelGround() => Shape(
                "mud_road_v2", "$piece_levelground", smooth: true, paint: true, paintType: 0);
            TerrainBrushShape Pathen() => Shape(
                "path_v2", "$piece_path", paint: true, paintType: 0);
            TerrainBrushShape PavedRoad() => Shape(
                "paved_road_v2", "$piece_pavedroad", smooth: true, paint: true, paintType: 2,
                station: true, resources: 1, single: 1, stone: 1);

            Run("exact vanilla smooth-and-dirt action is Level Ground", () =>
                Equal(TerrainBrushKind.LevelGround, VanillaTerrainBrushClassifier.Classify(LevelGround())));
            Run("exact vanilla dirt paint action is Pathen", () =>
                Equal(TerrainBrushKind.Pathen, VanillaTerrainBrushClassifier.Classify(Pathen())));
            Run("exact vanilla station-backed paved action is Paved Road", () =>
                Equal(TerrainBrushKind.PavedRoad, VanillaTerrainBrushClassifier.Classify(PavedRoad())));
            Run("Level Ground with level channel is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "mud_road_v2", "$piece_levelground", level: true, smooth: true, paint: true, paintType: 0))));
            Run("Raise Ground is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "mud_road_v2", "$piece_levelground", raise: true, smooth: true, paint: true, paintType: 0))));
            Run("cultivator paint is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 1))));
            Run("Pathen live shape tolerates Valheim prefab object renaming", () =>
                Equal(TerrainBrushKind.Pathen, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path", "$piece_path", paint: true, paintType: 0))));
            Run("wrong localized identity is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_modded", paint: true, paintType: 0))));
            Run("rotatable action is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 0, canRotate: true))));
            Run("ordinary hammer piece is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "wood_wall", "$piece_woodwall", terrainOps: 0, rootTerrainOp: false))));
            Run("child Piece ambiguity is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 0, pieces: 2))));
            Run("missing root TerrainOp is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 0, rootTerrainOp: false))));
            Run("extra TerrainOp is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 0, terrainOps: 2))));
            Run("legacy TerrainModifier addition is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "path_v2", "$piece_path", paint: true, paintType: 0, terrainModifiers: 1))));
            Run("paved paint without vanilla recipe shape is excluded", () =>
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "paved_road_v2", "$piece_pavedroad", smooth: true, paint: true, paintType: 2))));

            Run("Level Ground radius begins at vanilla three metres", () =>
                Near(3f, new TerrainRadiusSelection(TerrainBrushKind.LevelGround, 3f).Radius));
            Run("Pathen radius begins at vanilla two metres", () =>
                Near(2f, new TerrainRadiusSelection(TerrainBrushKind.Pathen, 2f).Radius));
            Run("Paved Road radius begins at vanilla three metres", () =>
                Near(3f, new TerrainRadiusSelection(TerrainBrushKind.PavedRoad, 3f).Radius));
            Run("scroll up increases radius by half a metre", () =>
                Near(3.5f, new TerrainRadiusSelection(3f).Scroll(1f).Radius));
            Run("scroll down decreases radius by half a metre", () =>
                Near(1.5f, new TerrainRadiusSelection(2f).Scroll(-1f).Radius));
            Run("zero scroll leaves radius unchanged", () =>
                Near(3f, new TerrainRadiusSelection(3f).Scroll(0f).Radius));
            Run("radius is bounded at one metre", () =>
                Near(1f, new TerrainRadiusSelection(1f).Scroll(-1f).Radius));
            Run("Level Ground radius is bounded at eight metres", () =>
                Near(8f, new TerrainRadiusSelection(TerrainBrushKind.LevelGround, 8f).Scroll(1f).Radius));
            Run("Level Ground constructor clamps oversized values to eight metres", () =>
                Near(8f, new TerrainRadiusSelection(TerrainBrushKind.LevelGround, 10f).Radius));
            Run("Pathen radius remains bounded at ten metres", () =>
                Near(10f, new TerrainRadiusSelection(TerrainBrushKind.Pathen, 10f).Scroll(1f).Radius));
            Run("Paved Road radius remains bounded at ten metres", () =>
                Near(10f, new TerrainRadiusSelection(TerrainBrushKind.PavedRoad, 10f).Scroll(1f).Radius));
            Run("non-finite radius returns to safe vanilla fallback", () =>
            {
                Near(2f, new TerrainRadiusSelection(float.NaN).Radius);
                Near(2f, new TerrainRadiusSelection(float.PositiveInfinity).Radius);
            });
            Run("selection quantizes to half-metre steps", () =>
                Near(2.5f, new TerrainRadiusSelection(2.31f).Radius));
            Run("indicator scale is proportional to the selected action baseline", () =>
                Near(2f, new TerrainRadiusSelection(6f).ScaleFor(3f)));
            Run("inactive vanilla marker remains eligible for transform synchronization", () =>
                Equal(true, TerrainIndicatorRouting.CanSynchronize(markerExists: true, activeInHierarchy: false)));
            Run("missing marker fails closed", () =>
                Equal(false, TerrainIndicatorRouting.CanSynchronize(markerExists: false, activeInHierarchy: true)));
            Run("pinned Pathen prefab evidence proves the inactive rotated direct marker", () =>
            {
                var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "path_v2-prefab.json");
                using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
                var root = fixture.RootElement.GetProperty("root");
                var marker = fixture.RootElement.GetProperty("ghost_only");
                Equal("path_v2", root.GetProperty("name").GetString()!);
                Equal(true, root.GetProperty("active").GetBoolean());
                Equal("_GhostOnly", marker.GetProperty("name").GetString()!);
                Equal(true, marker.GetProperty("direct_child_of_root").GetBoolean());
                Equal(false, marker.GetProperty("active").GetBoolean());
                var scale = marker.GetProperty("local_scale");
                Near(4f, scale[0].GetSingle());
                Near(4f, scale[1].GetSingle());
                Near(1f, scale[2].GetSingle());
                var rotation = marker.GetProperty("local_rotation_quaternion_xyzw");
                Near(0.7071068f, rotation[0].GetSingle());
                Near(0f, rotation[1].GetSingle());
                Near(0f, rotation[2].GetSingle());
                Near(0.7071068f, rotation[3].GetSingle());

                var particle = marker.GetProperty("particle_system");
                Equal("Particle System", particle.GetProperty("name").GetString()!);
                Equal(true, particle.GetProperty("direct_child_of_ghost_only").GetBoolean());
                Equal("Local", particle.GetProperty("scaling_mode").GetString()!);
                Equal(1, particle.GetProperty("scaling_mode_value").GetInt32());
                var particleScale = particle.GetProperty("local_scale");
                Near(1f, particleScale[0].GetSingle());
                Near(1f, particleScale[1].GetSingle());
                Near(1f, particleScale[2].GetSingle());

                var scaled = new TerrainIndicatorScale(
                    scale[0].GetSingle(), scale[1].GetSingle(), scale[2].GetSingle()).ScaleUniformly(2.5f);
                Near(10f, scaled.X);
                Near(10f, scaled.Y);
                Near(2.5f, scaled.Z);
                var scaledParticle = new TerrainIndicatorScale(
                    particleScale[0].GetSingle(), particleScale[1].GetSingle(), particleScale[2].GetSingle()).ScaleUniformly(2.5f);
                Near(2.5f, scaledParticle.X);
                Near(2.5f, scaledParticle.Y);
                Near(2.5f, scaledParticle.Z);
            });
            Run("GhostOnly marker always scales its own transform", () =>
                Equal(true, TerrainIndicatorRouting.ShouldScaleOwnTransform(
                    isGhostOnlyMarker: true, isLocalScalingParticle: false)));
            Run("nested Local particle scales its own transform", () =>
                Equal(true, TerrainIndicatorRouting.ShouldScaleOwnTransform(
                    isGhostOnlyMarker: false, isLocalScalingParticle: true)));
            Run("nonlocal descendant inherits marker scale without second scaling", () =>
                Equal(false, TerrainIndicatorRouting.ShouldScaleOwnTransform(
                    isGhostOnlyMarker: false, isLocalScalingParticle: false)));
            Run("invalid indicator factor leaves the marker unchanged", () =>
            {
                var scaled = new TerrainIndicatorScale(4f, 4f, 1f).ScaleUniformly(float.NaN);
                Near(4f, scaled.X);
                Near(4f, scaled.Y);
                Near(1f, scaled.Z);
            });
            Run("dual-source mutation applies and restores both settings objects", () =>
            {
                var selected = FakeTerrainRadiusTarget.Pathen();
                var registered = FakeTerrainRadiusTarget.Pathen();
                var mutation = new TerrainRadiusMutationSession(new[] { selected, registered });
                Equal(2, mutation.TargetCount);
                mutation.Apply(5f);
                Near(5f, selected.PaintRadius);
                Near(5f, registered.PaintRadius);
                Equal(false, mutation.Restore());
                Near(2f, selected.PaintRadius);
                Near(2f, registered.PaintRadius);
            });
            Run("Level Ground clone search and registered effect both use the selected radius before restore", () =>
            {
                var selectedPrefab = FakeTerrainRadiusTarget.LevelGround();
                var registeredEffect = FakeTerrainRadiusTarget.LevelGround();
                var mutation = new TerrainRadiusMutationSession(new[] { selectedPrefab, registeredEffect });
                mutation.Apply(6f);
                // Unity Instantiate copies the selected prefab before TerrainOp.Awake asks for its outer radius.
                Near(6f, selectedPrefab.MaximumActiveRadius());
                // TerrainOp.Settings.Deserialize resolves this distinct ObjectDB source for the real operation.
                Near(6f, registeredEffect.MaximumActiveRadius());
                Equal(false, mutation.Restore());
                Near(3f, selectedPrefab.MaximumActiveRadius());
                Near(3f, registeredEffect.MaximumActiveRadius());
            });
            Run("Pathen clone search and registered effect both use the selected radius before restore", () =>
            {
                var selectedPrefab = FakeTerrainRadiusTarget.Pathen();
                var registeredEffect = FakeTerrainRadiusTarget.Pathen();
                var mutation = new TerrainRadiusMutationSession(new[] { selectedPrefab, registeredEffect });
                mutation.Apply(5f);
                Near(5f, selectedPrefab.MaximumActiveRadius());
                Near(5f, registeredEffect.MaximumActiveRadius());
                Equal(false, mutation.Restore());
                Near(2f, selectedPrefab.MaximumActiveRadius());
                Near(2f, registeredEffect.MaximumActiveRadius());
            });
            Run("the 0.2.1 selected-only target leaves the resolved effect at vanilla radius", () =>
            {
                var selectedPrefab = FakeTerrainRadiusTarget.LevelGround();
                var registeredEffect = FakeTerrainRadiusTarget.LevelGround();
                var oldMutationShape = new TerrainRadiusMutationSession(new[] { selectedPrefab });
                oldMutationShape.Apply(6f);
                Near(6f, selectedPrefab.MaximumActiveRadius());
                Near(3f, registeredEffect.MaximumActiveRadius());
                oldMutationShape.Restore();

                selectedPrefab = FakeTerrainRadiusTarget.Pathen();
                registeredEffect = FakeTerrainRadiusTarget.Pathen();
                oldMutationShape = new TerrainRadiusMutationSession(new[] { selectedPrefab });
                oldMutationShape.Apply(5f);
                Near(5f, selectedPrefab.MaximumActiveRadius());
                Near(2f, registeredEffect.MaximumActiveRadius());
                oldMutationShape.Restore();
            });
            Run("0.2.2 failure is corrected at the authoritative Level Ground settings boundary", () =>
            {
                var selectedPrefab = FakeTerrainRadiusTarget.LevelGround();
                var authoritativeSettings = FakeTerrainRadiusTarget.LevelGround();
                var prefabMutation = new TerrainRadiusMutationSession(new[] { selectedPrefab });
                prefabMutation.Apply(6f);

                // The old implementation could adjust the clone source while the
                // independently resolved settings consumed by DoOperation stayed vanilla.
                Near(6f, selectedPrefab.MaximumActiveRadius());
                Near(3f, authoritativeSettings.MaximumActiveRadius());
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, authoritativeSettings, 6f));

                var operationMutation = new TerrainRadiusMutationSession(new[] { authoritativeSettings });
                operationMutation.Apply(6f);
                Near(6f, authoritativeSettings.SmoothRadius);
                Near(6f, authoritativeSettings.PaintRadius);
                Equal(false, operationMutation.Restore());
                Near(3f, authoritativeSettings.MaximumActiveRadius());
                Equal(false, prefabMutation.Restore());
            });
            Run("authoritative Level Ground supports a smaller one-metre operation", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, settings, 1f));
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                mutation.Apply(1f);
                Near(1f, settings.SmoothRadius);
                Near(1f, settings.PaintRadius);
                mutation.Restore();
            });
            Run("authoritative guard accepts already-adjusted Level Ground settings", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                mutation.Apply(5.5f);
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, settings, 5.5f));
                mutation.Restore();
            });
            Run("authoritative guard applies the shared mechanism to Pathen", () =>
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.Pathen, FakeTerrainRadiusTarget.Pathen(), 5f)));
            Run("authoritative guard preserves the Paved Road channel ratio", () =>
            {
                var settings = FakeTerrainRadiusTarget.PavedRoad();
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.PavedRoad, settings, 6f));
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                mutation.Apply(6f);
                Near(6f, settings.SmoothRadius);
                Near(4.4f, settings.PaintRadius);
                Equal(true, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.PavedRoad, settings, 6f));
                mutation.Restore();
            });
            Run("authoritative guard excludes Raise Ground", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                settings.RaiseActive = true;
                Equal(false, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, settings, 6f));
            });
            Run("authoritative guard excludes altered Level Ground radii", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                settings.PaintRadius = 2.75f;
                Equal(false, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, settings, 6f));
            });
            Run("authoritative guard excludes wrong paint semantics", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                settings.PaintType = 1;
                Equal(false, TerrainRadiusOperationGuard.MatchesAuthoritativeSettings(
                    TerrainBrushKind.LevelGround, settings, 6f));
            });
            Run("shared settings identity is mutated only once", () =>
            {
                var settings = FakeTerrainRadiusTarget.Pathen();
                var mutation = new TerrainRadiusMutationSession(new[] { settings, settings });
                Equal(1, mutation.TargetCount);
                mutation.Apply(4f);
                Near(4f, settings.PaintRadius);
                Equal(false, mutation.Restore());
                Near(2f, settings.PaintRadius);
            });
            Run("restoration preserves a conflicting runtime field", () =>
            {
                var settings = FakeTerrainRadiusTarget.PavedRoad();
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                mutation.Apply(6f);
                Near(6f, settings.SmoothRadius);
                Near(4.4f, settings.PaintRadius);
                settings.PaintRadius = 9f;
                Equal(true, mutation.Restore());
                Near(3f, settings.SmoothRadius);
                Near(9f, settings.PaintRadius);
            });
            Run("mutation never changes disabled terrain channels", () =>
            {
                var settings = FakeTerrainRadiusTarget.Pathen();
                settings.LevelRadius = 5f;
                settings.RaiseRadius = 6f;
                settings.SmoothRadius = 7f;
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                mutation.Apply(4f);
                Near(5f, settings.LevelRadius);
                Near(6f, settings.RaiseRadius);
                Near(7f, settings.SmoothRadius);
                Near(4f, settings.PaintRadius);
                mutation.Restore();
            });
            Run("partial application rolls back fields already changed", () =>
            {
                var settings = FakeTerrainRadiusTarget.LevelGround();
                settings.ThrowOnPaintWrite = true;
                var mutation = new TerrainRadiusMutationSession(new[] { settings });
                try
                {
                    mutation.Apply(6f);
                    throw new InvalidOperationException("Expected the injected paint write failure.");
                }
                catch (ApplicationException)
                {
                    Near(3f, settings.SmoothRadius);
                    Near(3f, settings.PaintRadius);
                }
            });
            Run("invalid indicator baseline fails closed to unit scale", () =>
                Near(1f, new TerrainRadiusSelection(4f).ScaleFor(0f)));
            Run("Paved Road smooth and paint radii preserve their vanilla ratio", () =>
            {
                var scaled = new TerrainRadiusValues(7f, 8f, 3f, 2.2f)
                    .ScaleActive(false, false, true, true, 6f);
                Near(7f, scaled.Level);
                Near(8f, scaled.Raise);
                Near(6f, scaled.Smooth);
                Near(4.4f, scaled.Paint);
            });
            Run("Level Ground active radii scale together", () =>
            {
                var scaled = new TerrainRadiusValues(5f, 6f, 3f, 3f)
                    .ScaleActive(false, false, true, true, 4.5f);
                Near(5f, scaled.Level);
                Near(6f, scaled.Raise);
                Near(4.5f, scaled.Smooth);
                Near(4.5f, scaled.Paint);
            });
            Run("Pathen changes only its active paint radius", () =>
            {
                var scaled = new TerrainRadiusValues(5f, 6f, 7f, 2f)
                    .ScaleActive(false, false, false, true, 4f);
                Near(5f, scaled.Level);
                Near(6f, scaled.Raise);
                Near(7f, scaled.Smooth);
                Near(4f, scaled.Paint);
            });
            Run("Pathen indicator and paint effect share the same proportional scale", () =>
            {
                var radius = new TerrainRadiusSelection(TerrainBrushKind.Pathen, 5f);
                Near(2.5f, radius.ScaleFor(2f));
                var scaled = new TerrainRadiusValues(9f, 8f, 7f, 2f)
                    .ScaleActive(false, false, false, true, radius.Radius);
                Near(5f, scaled.Paint);
            });
            Run("pinned HUD proves the 0.2.4 template belongs to the gamepad branch", () =>
            {
                var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "key-hints-ui.json");
                using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
                var root = fixture.RootElement;
                var source = root.GetProperty("alternative_placing_label");
                var gamepad = root.GetProperty("gamepad_container");
                Equal("m_buildAlternativePlacingKey", source.GetProperty("key_hints_field").GetString()!);
                Equal("Gamepad", source.GetProperty("direct_child_of").GetString()!);
                Equal("m_gamepadHint", gamepad.GetProperty("ui_input_hint_field").GetString()!);
                Equal(false, gamepad.GetProperty("serialized_active").GetBoolean());
            });
            Run("pinned HUD proves a compatible active keyboard layout target", () =>
            {
                var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "key-hints-ui.json");
                using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
                var root = fixture.RootElement;
                var source = root.GetProperty("alternative_placing_label");
                var gamepad = root.GetProperty("gamepad_container");
                var keyboard = root.GetProperty("keyboard_container");
                Equal("m_mouseKeyboardHint", keyboard.GetProperty("ui_input_hint_field").GetString()!);
                Equal(true, keyboard.GetProperty("serialized_active").GetBoolean());
                Equal("UnityEngine.UI.HorizontalLayoutGroup", gamepad.GetProperty("layout").GetString()!);
                Equal(gamepad.GetProperty("layout").GetString()!, keyboard.GetProperty("layout").GetString()!);
                Equal(true, source.GetProperty("has_layout_element").GetBoolean());
                Equal(false, source.GetProperty("layout_element_ignore_layout").GetBoolean());
                Equal(true, keyboard.GetProperty("direct_child_of_build_hints").GetBoolean());
            });
            Run("brush-size hint accepts only the validated keyboard sibling layout", () =>
                Equal(true, TerrainRadiusHintLayoutRouting.CanAttach(
                    true, true, true, true, true, true, true)));
            Run("brush-size hint rejects every missing or changed hierarchy edge", () =>
            {
                for (var missing = 0; missing < 7; missing++)
                {
                    var contract = new[] { true, true, true, true, true, true, true };
                    contract[missing] = false;
                    Equal(false, TerrainRadiusHintLayoutRouting.CanAttach(
                        contract[0], contract[1], contract[2], contract[3],
                        contract[4], contract[5], contract[6]));
                }
            });
            Run("brush-size hint appears for an exact eligible keyboard selection", () =>
                Equal(true, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, true, TerrainBrushKind.LevelGround)));
            Run("brush-size hint supports Pathen and Paved Road", () =>
            {
                Equal(true, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, true, TerrainBrushKind.Pathen));
                Equal(true, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, true, TerrainBrushKind.PavedRoad));
            });
            Run("brush-size hint excludes Raise Ground and unrelated actions", () =>
            {
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, true, TerrainBrushKind.None));
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, true, (TerrainBrushKind)99));
            });
            Run("brush-size hint is not added to controller controls", () =>
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, true, false, true, TerrainBrushKind.LevelGround)));
            Run("brush-size hint is hidden while the piece selector is open", () =>
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, true, true, TerrainBrushKind.LevelGround)));
            Run("brush-size hint fails closed for invalid brush bindings", () =>
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, false, false, false, false, TerrainBrushKind.LevelGround)));
            Run("brush-size hint is hidden outside live placement", () =>
            {
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    false, true, false, false, false, true, TerrainBrushKind.LevelGround));
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, false, false, false, false, true, TerrainBrushKind.LevelGround));
                Equal(false, TerrainRadiusControlHintRouting.ShouldShow(
                    true, true, true, false, false, true, TerrainBrushKind.LevelGround));
            });
            Run("camera wheel is suppressed for successful Alt hoe adjustments", () =>
            {
                var radius = new TerrainRadiusSelection(TerrainBrushKind.Pathen, 2f);
                Near(2.5f, radius.Scroll(1f).Radius);
                Equal(true, CameraWheelRouting.ShouldSuppressZoom(true, true, false, true, true, false));
            });
            Run("camera wheel remains suppressed at radius bounds and no-op adjustments", () =>
            {
                var radius = new TerrainRadiusSelection(TerrainBrushKind.LevelGround, 8f);
                Near(8f, radius.Scroll(1f).Radius);
                Equal(true, CameraWheelRouting.ShouldSuppressZoom(true, true, false, true, true, false));
            });
            Run("camera wheel remains suppressed for ineligible vanilla hoe actions", () =>
            {
                Equal(TerrainBrushKind.None, VanillaTerrainBrushClassifier.Classify(Shape(
                    "raise_v2", "$piece_raise", raise: true, paint: false, paintType: 0)));
                Equal(true, CameraWheelRouting.ShouldSuppressZoom(true, true, false, true, true, false));
            });
            Run("camera wheel is not suppressed without Alt", () =>
                Equal(false, CameraWheelRouting.ShouldSuppressZoom(true, true, false, true, false, false)));
            Run("camera wheel is not suppressed for another tool", () =>
                Equal(false, CameraWheelRouting.ShouldSuppressZoom(true, true, false, false, true, false)));
            Run("camera wheel is not suppressed while the build selection UI is open", () =>
                Equal(false, CameraWheelRouting.ShouldSuppressZoom(true, true, false, true, true, true)));
            Run("camera wheel is not suppressed outside place mode", () =>
                Equal(false, CameraWheelRouting.ShouldSuppressZoom(true, false, false, true, true, false)));
            Run("missing active radius leaves all values untouched", () =>
            {
                var values = new TerrainRadiusValues(2f, 3f, 4f, 5f);
                var scaled = values.ScaleActive(false, false, false, false, 8f);
                Near(2f, scaled.Level);
                Near(3f, scaled.Raise);
                Near(4f, scaled.Smooth);
                Near(5f, scaled.Paint);
            });
        }

        private static void RuntimeSafetyTests()
        {
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            Run("exact runtime method contract is accepted", () =>
                Equal(1, ExactRuntimeContract.FindMethods(
                    typeof(FakeRuntimeContract), "Target", methods, typeof(int), new[] { typeof(string) }).Length));
            Run("runtime method shape mismatch is rejected", () =>
                Equal(0, ExactRuntimeContract.FindMethods(
                    typeof(FakeRuntimeContract), "Target", methods, typeof(void), new[] { typeof(string) }).Length));
            Run("ambiguous runtime field contract is rejected", () =>
                Equal(2, ExactRuntimeContract.FindFields(
                    typeof(DerivedAmbiguousContract), "Value",
                    BindingFlags.Instance | BindingFlags.Public, typeof(int)).Length));
            Run("failed install executes every rollback step", () =>
            {
                var firstRollbackRan = false;
                var secondRollbackRan = false;
                try
                {
                    TransactionalInstall.Run(
                        () => throw new InvalidOperationException("install"),
                        () =>
                        {
                            firstRollbackRan = true;
                            throw new InvalidOperationException("cleanup");
                        },
                        () => secondRollbackRan = true);
                    throw new InvalidOperationException("Expected transactional install failure.");
                }
                catch (AggregateException exception)
                {
                    Equal(2, exception.InnerExceptions.Count);
                    Equal(true, firstRollbackRan);
                    Equal(true, secondRollbackRan);
                }
            });
            Run("successful install does not run rollback", () =>
            {
                var installed = false;
                var rollbackRan = false;
                TransactionalInstall.Run(() => installed = true, () => rollbackRan = true);
                Equal(true, installed);
                Equal(false, rollbackRan);
            });
        }

        private sealed class FakeRuntimeContract
        {
            public int Target(string value) => value.Length;
        }

        private class BaseAmbiguousContract
        {
            public int Value = 0;
        }

        private sealed class DerivedAmbiguousContract : BaseAmbiguousContract
        {
            public new int Value = 0;
        }

        private sealed class FakeTerrainRadiusTarget : ITerrainRadiusMutationTarget
        {
            private float _paintRadius;

            private FakeTerrainRadiusTarget()
            {
                Identity = new object();
            }

            internal static FakeTerrainRadiusTarget Pathen() => new FakeTerrainRadiusTarget
            {
                PaintActive = true,
                PaintType = VanillaTerrainBrushClassifier.DirtPaintType,
                PaintRadius = 2f
            };

            internal static FakeTerrainRadiusTarget PavedRoad() => new FakeTerrainRadiusTarget
            {
                SmoothActive = true,
                PaintActive = true,
                PaintType = VanillaTerrainBrushClassifier.PavedPaintType,
                SmoothRadius = 3f,
                PaintRadius = 2.2f
            };

            internal static FakeTerrainRadiusTarget LevelGround() => new FakeTerrainRadiusTarget
            {
                SmoothActive = true,
                PaintActive = true,
                PaintType = VanillaTerrainBrushClassifier.DirtPaintType,
                SmoothRadius = 3f,
                PaintRadius = 3f
            };

            public object Identity { get; }
            public bool LevelActive { get; set; }
            public bool RaiseActive { get; set; }
            public bool SmoothActive { get; set; }
            public bool PaintActive { get; set; }
            public int PaintType { get; set; }
            public float LevelRadius { get; set; }
            public float RaiseRadius { get; set; }
            public float SmoothRadius { get; set; }
            public float PaintRadius
            {
                get => _paintRadius;
                set
                {
                    if (ThrowOnPaintWrite) throw new ApplicationException("Injected paint write failure.");
                    _paintRadius = value;
                }
            }
            internal bool ThrowOnPaintWrite { get; set; }

            internal float MaximumActiveRadius() => new TerrainRadiusValues(
                LevelRadius, RaiseRadius, SmoothRadius, PaintRadius).MaximumActiveRadius(
                LevelActive, RaiseActive, SmoothActive, PaintActive);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
                throw;
            }
        }

        private static void Equal<T>(T expected, T actual) where T : notnull
        {
            if (!expected.Equals(actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }

        private static void Near(float expected, float actual)
        {
            if (Math.Abs(expected - actual) > 0.0001f)
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }
    }
}
