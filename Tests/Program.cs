// Entry point for the offline verification harness.
//
// It compiles the production sources against minimal game doubles and exercises the
// measurement, dashboard and history logic. It does NOT prove RimWorld API compatibility,
// in-game behaviour, rendering or performance: those require the real game assemblies.
using System;
using System.Collections.Generic;
using PowerGridManager.Tests;

namespace PowerGridManager
{
    public static class Program
    {
        public static int Main()
        {
            var phases = new List<KeyValuePair<string, Func<int>>>
            {
                new KeyValuePair<string, Func<int>>("Phase 2 monitoring", MonitoringTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 3 dashboard", DashboardTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 4 history", HistoryTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 5 diagnostics", DiagnosticsTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 6 forecast", ForecastTests.Run),
                new KeyValuePair<string, Func<int>>("Localization/metadata", LocalizationTests.Run)
            };

            var failures = new List<string>();
            foreach (KeyValuePair<string, Func<int>> phase in phases)
            {
                try
                {
                    phase.Value();
                }
                catch (Exception exception)
                {
                    failures.Add(phase.Key + ": " + exception.Message);
                    Console.WriteLine("FAILED " + phase.Key);
                    Console.WriteLine(exception);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Total checks: " + TestAssert.Checks);
            if (failures.Count == 0)
            {
                Console.WriteLine("ALL CHECKS PASSED (game doubles; not in-game validation).");
                return 0;
            }

            Console.WriteLine("FAILURES:");
            foreach (string failure in failures) Console.WriteLine(" - " + failure);
            return 1;
        }
    }
}
