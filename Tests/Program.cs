// Entry point for the offline verification harness.
//
// It compiles the production sources against minimal game doubles and exercises the
// measurement, dashboard, history, diagnostics, forecast and management logic. It does NOT
// prove RimWorld API compatibility, in-game behaviour, rendering or frame-time performance:
// those require the real game assemblies and a running game.
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
                new KeyValuePair<string, Func<int>>("Heatmap performance", HeatmapTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 4 history", HistoryTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 5 diagnostics", DiagnosticsTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 6 forecast", ForecastTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 7 management", ManagementTests.Run),
                new KeyValuePair<string, Func<int>>("Phase 8 compatibility", CompatibilityTests.Run),
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
                    string failure = phase.Key + ": " + exception.Message;
                    failures.Add(failure);
                    Console.WriteLine("FAILED " + phase.Key);
                    Console.WriteLine(exception);
                    // GitHub Actions has no console output in the checks API, so mirror the
                    // failure into an annotation that reviewers (and automation) can read.
                    Annotate("::error::" + EscapeAnnotation(failure + " | " + exception.StackTrace));
                }
            }

            Console.WriteLine();
            Console.WriteLine("Total checks: " + TestAssert.Checks);
            if (failures.Count == 0)
            {
                Console.WriteLine("ALL CHECKS PASSED (game doubles; not in-game validation).");
                Annotate("::notice::" + EscapeAnnotation("Offline logic harness: " + TestAssert.Checks +
                    " checks passed against game doubles (not in-game validation)."));
                return 0;
            }

            Console.WriteLine("FAILURES:");
            foreach (string failure in failures) Console.WriteLine(" - " + failure);
            Annotate("::error::" + EscapeAnnotation(
                failures.Count + " test phase(s) failed: " + string.Join("; ", failures)));
            return 1;
        }

        private static void Annotate(string annotation)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true",
                StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(annotation);
            }
        }

        private static string EscapeAnnotation(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
        }
    }
}
