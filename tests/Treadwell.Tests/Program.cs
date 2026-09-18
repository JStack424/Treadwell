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
            RuntimeSafetyTests();
            Console.WriteLine(_passed + "/61 core tests passed");
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
                var shape = Candidate(terrainModifierCount: 2, pavedTerrainModifierCount: 1);
                Equal(true, shape.IsSemanticCandidate);
            });
            Run("unique semantic candidate wins among unrelated entries", () =>
            {
                var selection = Select(
                    Candidate(pavedTerrainModifierCount: 0),
                    Candidate(),
                    Candidate(hasStationRequirement: false));
                Equal(PavedRoadDiscoveryOutcome.Unique, selection.Outcome);
                Equal(1, selection.CandidateIndex);
            });
            Run("zero semantic candidates fails closed", () =>
            {
                var selection = Select(Candidate(pavedTerrainModifierCount: 0));
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
                Equal(false, Candidate(pavedTerrainModifierCount: 0).IsSemanticCandidate));
            Run("multiple paved operations are rejected", () =>
                Equal(false, Candidate(terrainModifierCount: 2, pavedTerrainModifierCount: 2).IsSemanticCandidate));
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
                Equal(true, Candidate(terrainModifierCount: 3, pavedTerrainModifierCount: 1).IsSemanticCandidate));
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
            Run("null piece is rejected without mutation", () =>
                Equal(StationOverrideApplyResult.InvalidPiece, NewStationOverride().Apply(null!)));
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
            int terrainModifierCount = 1,
            int pavedTerrainModifierCount = 1,
            bool hasStationRequirement = true,
            int resourceRequirementCount = 1,
            int singleUnitResourceRequirementCount = 1,
            int singleUnitStoneResourceRequirementCount = 1)
            => new PavedRoadCandidateShape(
                pieceComponentCount,
                hasRootPiece,
                terrainModifierCount,
                pavedTerrainModifierCount,
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
