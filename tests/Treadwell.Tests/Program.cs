using System;
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
            Console.WriteLine(_passed + "/29 core tests passed");
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
