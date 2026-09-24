using System;
using System.Reflection;
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
            PavedRoadPlacementTests();
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

        private static void PavedRoadPlacementTests()
        {
            Run("semantic candidate does not depend on prefab or display names", () =>
            {
                var selection = Select(Candidate());
                Equal(PavedRoadDiscoveryOutcome.Unique, selection.Outcome);
                Equal(0, selection.CandidateIndex);
            });
            Run("nested paved terrain operation is eligible", () =>
            {
                var shape = Candidate(terrainOperationCount: 2, pavedTerrainOperationCount: 1);
                Equal(true, shape.IsSemanticCandidate);
            });
            Run("unique semantic candidate wins among unrelated entries", () =>
            {
                var selection = Select(
                    Candidate(pavedTerrainOperationCount: 0),
                    Candidate(),
                    Candidate(hasStationRequirement: false));
                Equal(PavedRoadDiscoveryOutcome.Unique, selection.Outcome);
                Equal(1, selection.CandidateIndex);
            });
            Run("zero semantic candidates fails closed", () =>
            {
                var selection = Select(Candidate(pavedTerrainOperationCount: 0));
                Equal(PavedRoadDiscoveryOutcome.None, selection.Outcome);
                Equal(-1, selection.CandidateIndex);
                Equal(0, selection.CandidateCount);
            });
            Run("multiple semantic candidates fail closed", () =>
            {
                var selection = Select(Candidate(), Candidate());
                Equal(PavedRoadDiscoveryOutcome.Ambiguous, selection.Outcome);
                Equal(-1, selection.CandidateIndex);
                Equal(2, selection.CandidateCount);
            });
            Run("stationless unowned entry is rejected", () =>
                Equal(false, Candidate(hasStationRequirement: false).IsSemanticCandidate));
            Run("owned station removal remains discoverable", () =>
                Equal(true, Candidate(hasStationRequirement: true).IsSemanticCandidate));
            Run("non-paved terrain operation is rejected", () =>
                Equal(false, Candidate(pavedTerrainOperationCount: 0).IsSemanticCandidate));
            Run("multiple paved operations are rejected", () =>
                Equal(false, Candidate(terrainOperationCount: 2, pavedTerrainOperationCount: 2).IsSemanticCandidate));
            Run("missing resource requirement is rejected", () =>
                Equal(false, Candidate(resourceRequirementCount: 0, singleUnitResourceRequirementCount: 0).IsSemanticCandidate));
            Run("multiple resource requirements are rejected", () =>
                Equal(false, Candidate(resourceRequirementCount: 2, singleUnitResourceRequirementCount: 2).IsSemanticCandidate));
            Run("non-unit stone resource amount is rejected", () =>
                Equal(false, Candidate(singleUnitResourceRequirementCount: 0, singleUnitStoneResourceRequirementCount: 0).IsSemanticCandidate));
            Run("one-unit non-stone resource is rejected", () =>
                Equal(false, Candidate(singleUnitResourceRequirementCount: 1, singleUnitStoneResourceRequirementCount: 0).IsSemanticCandidate));
            Run("multiple Piece components are rejected", () =>
                Equal(false, Candidate(pieceComponentCount: 2).IsSemanticCandidate));
            Run("single Piece that is not on the table-entry root is rejected", () =>
                Equal(false, Candidate(hasRootPiece: false).IsSemanticCandidate));
            Run("additional non-paved terrain helpers do not hide one paved operation", () =>
                Equal(true, Candidate(terrainOperationCount: 3, pavedTerrainOperationCount: 1).IsSemanticCandidate));
            Run("null candidate list is rejected", () =>
            {
                try
                {
                    PavedRoadCandidateSelector.Select(null!);
                    throw new InvalidOperationException("Expected ArgumentNullException.");
                }
                catch (ArgumentNullException)
                {
                }
            });
            Run("selected paved road removes whatever station object is attached", () =>
            {
                var station = new FakeStation("runtime-station-with-unexpected-identity");
                var piece = new FakePiece(station);
                var stationOverride = NewStationOverride();
                Equal(StationOverrideApplyResult.Removed, stationOverride.Apply(piece));
                Equal(true, piece.Station == null);
            });
            Run("station object names are never consulted", () =>
            {
                var station = new FakeStation(null);
                var piece = new FakePiece(station);
                Equal(StationOverrideApplyResult.Removed, NewStationOverride().Apply(piece));
                Equal(true, piece.Station == null);
            });
            Run("restore returns the exact captured station object", () =>
            {
                var station = ExactStation();
                var piece = new FakePiece(station);
                var stationOverride = NewStationOverride();
                stationOverride.Apply(piece);
                Equal(StationOverrideRestoreResult.Restored, stationOverride.Restore());
                Equal(station, piece.Station!);
            });
            Run("repeated apply is idempotent", () =>
            {
                var piece = new FakePiece(ExactStation());
                var stationOverride = NewStationOverride();
                Equal(StationOverrideApplyResult.Removed, stationOverride.Apply(piece));
                Equal(StationOverrideApplyResult.AlreadyAbsent, stationOverride.Apply(piece));
                Equal(true, stationOverride.IsAppliedTo(piece));
            });
            Run("piece-table replacement restores old piece before changing new piece", () =>
            {
                var firstStation = ExactStation();
                var firstPiece = new FakePiece(firstStation);
                var secondStation = ExactStation();
                var secondPiece = new FakePiece(secondStation);
                var stationOverride = NewStationOverride();
                stationOverride.Apply(firstPiece);
                Equal(StationOverrideApplyResult.Removed, stationOverride.Apply(secondPiece));
                Equal(firstStation, firstPiece.Station!);
                Equal(true, secondPiece.Station == null);
                Equal(StationOverrideRestoreResult.Restored, stationOverride.Restore());
                Equal(secondStation, secondPiece.Station!);
            });
            Run("stationless runtime object does not displace live prefab ownership", () =>
            {
                var station = ExactStation();
                var prefab = new FakePiece(station);
                var stationless = new FakePiece(null);
                var stationOverride = NewStationOverride();
                stationOverride.Apply(prefab);
                Equal(StationOverrideApplyResult.AlreadyAbsent, stationOverride.Apply(stationless));
                Equal(true, stationOverride.IsAppliedTo(prefab));
                Equal(StationOverrideRestoreResult.Restored, stationOverride.Restore());
                Equal(station, prefab.Station!);
            });
            Run("active override refuses an unexpected replacement station", () =>
            {
                var piece = new FakePiece(ExactStation());
                var stationOverride = NewStationOverride();
                stationOverride.Apply(piece);
                var replacement = new FakeStation("other-mod-station");
                piece.Station = replacement;
                Equal(StationOverrideApplyResult.Conflict, stationOverride.Apply(piece));
                Equal(replacement, piece.Station!);
            });
            Run("restore never overwrites another runtime station change", () =>
            {
                var piece = new FakePiece(ExactStation());
                var stationOverride = NewStationOverride();
                stationOverride.Apply(piece);
                var replacement = new FakeStation("custom-station");
                piece.Station = replacement;
                Equal(StationOverrideRestoreResult.Conflict, stationOverride.Restore());
                Equal(replacement, piece.Station!);
                Equal(false, stationOverride.IsApplied);
            });
            Run("restore accepts an already restored original", () =>
            {
                var station = ExactStation();
                var piece = new FakePiece(station);
                var stationOverride = NewStationOverride();
                stationOverride.Apply(piece);
                piece.Station = station;
                Equal(StationOverrideRestoreResult.AlreadyRestored, stationOverride.Restore());
                Equal(station, piece.Station!);
                Equal(false, stationOverride.IsApplied);
            });
            Run("apply failure retains captured state for cleanup retry", () =>
            {
                var station = ExactStation();
                var piece = new FakePiece(station);
                var firstWrite = true;
                var stationOverride = new PavedRoadStationOverride<FakePiece, FakeStation>(
                    candidate => candidate.Station,
                    (candidate, value) =>
                    {
                        candidate.Station = value;
                        if (firstWrite)
                        {
                            firstWrite = false;
                            throw new InvalidOperationException("setter failed after mutation");
                        }
                    });

                try
                {
                    stationOverride.Apply(piece);
                    throw new InvalidOperationException("Expected setter failure.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "setter failed after mutation")
                {
                }

                Equal(true, stationOverride.IsAppliedTo(piece));
                Equal(true, piece.Station == null);
                Equal(StationOverrideRestoreResult.Restored, stationOverride.Restore());
                Equal(station, piece.Station!);
                Equal(false, stationOverride.IsApplied);
            });
            Run("restore failure retains captured state until a successful retry", () =>
            {
                var station = ExactStation();
                var piece = new FakePiece(station);
                var failRestore = true;
                var stationOverride = new PavedRoadStationOverride<FakePiece, FakeStation>(
                    candidate => candidate.Station,
                    (candidate, value) =>
                    {
                        candidate.Station = value;
                        if (value != null && failRestore)
                        {
                            failRestore = false;
                            throw new InvalidOperationException("restore failed after mutation");
                        }
                    });

                stationOverride.Apply(piece);
                try
                {
                    stationOverride.Restore();
                    throw new InvalidOperationException("Expected restore failure.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "restore failed after mutation")
                {
                }

                Equal(true, stationOverride.IsAppliedTo(piece));
                Equal(station, piece.Station!);
                Equal(StationOverrideRestoreResult.AlreadyRestored, stationOverride.Restore());
                Equal(false, stationOverride.IsApplied);
            });
            Run("null piece is rejected without mutation", () =>
                Equal(StationOverrideApplyResult.InvalidPiece, NewStationOverride().Apply(null!)));
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

        private static PavedRoadCandidateShape Candidate(
            int pieceComponentCount = 1,
            bool hasRootPiece = true,
            int terrainOperationCount = 1,
            int pavedTerrainOperationCount = 1,
            bool hasStationRequirement = true,
            int resourceRequirementCount = 1,
            int singleUnitResourceRequirementCount = 1,
            int singleUnitStoneResourceRequirementCount = 1)
            => new PavedRoadCandidateShape(
                pieceComponentCount,
                hasRootPiece,
                terrainOperationCount,
                pavedTerrainOperationCount,
                hasStationRequirement,
                resourceRequirementCount,
                singleUnitResourceRequirementCount,
                singleUnitStoneResourceRequirementCount);

        private static PavedRoadCandidateSelection Select(params PavedRoadCandidateShape[] candidates)
            => PavedRoadCandidateSelector.Select(candidates);

        private static PavedRoadStationOverride<FakePiece, FakeStation> NewStationOverride()
            => new PavedRoadStationOverride<FakePiece, FakeStation>(
                piece => piece.Station,
                (piece, station) => piece.Station = station);

        private static FakeStation ExactStation()
            => new FakeStation("captured-station");

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

        private sealed class FakePiece
        {
            internal FakePiece(FakeStation? station)
            {
                Station = station;
            }

            internal FakeStation? Station { get; set; }
        }

        private sealed class FakeStation
        {
            internal FakeStation(string? identity)
            {
                Identity = identity;
            }

            internal string? Identity { get; }
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
