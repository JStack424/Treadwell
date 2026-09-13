using System;
using Treadwell.Core;

namespace Treadwell.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static int Main()
        {
            Run("initial enabled state", () => Assert(new FeatureState(true).Enabled));
            Run("initial disabled state", () => Assert(!new FeatureState(false).Enabled));
            Run("no-op transition preserves instance", () =>
            {
                var state = new FeatureState(true);
                Assert(ReferenceEquals(state, state.WithEnabled(true)));
            });
            Run("state transition creates changed value", () =>
            {
                var state = new FeatureState(false).WithEnabled(true);
                Assert(state.Enabled && state.Equals(new FeatureState(true)));
            });
            Console.WriteLine(_passed + "/4 core tests passed");
            return 0;
        }

        private static void Run(string name, Action test)
        {
            try { test(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception exception) { Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); throw; }
        }

        private static void Assert(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed.");
        }
    }
}
