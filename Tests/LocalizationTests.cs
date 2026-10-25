// Localization and metadata checks: every PGM_ key referenced in source must exist in the
// shipped English keyed file, the keyed file must be well-formed XML, and the def/About XML
// must parse. This cannot validate translation quality, only completeness and structure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerGridManager.Tests;

namespace PowerGridManager
{
    public static class LocalizationTests
    {
        public static int Run()
        {
            TestAssert.UsePhase("localization");
            int start = TestAssert.Checks;
            string root = FindRepositoryRoot();
            TestAssert.NotNull(root, "repository root located");

            string keyedPath = Path.Combine(root, "Languages", "English", "Keyed", "PowerGridManager.xml");
            TestAssert.Check(File.Exists(keyedPath), "English keyed file exists: " + keyedPath);
            XDocument keyed = XDocument.Load(keyedPath);
            var defined = new HashSet<string>(keyed.Root.Elements().Select(e => e.Name.LocalName));

            var used = new HashSet<string>();
            var usedWithArgs = new HashSet<string>();
            Regex keyPattern = new Regex("\"(PGM_[A-Za-z0-9_]+)\"", RegexOptions.Compiled);
            Regex keyCallPattern = new Regex("\"(PGM_[A-Za-z0-9_]+)\"\\.Translate\\(([^)]*)\\)",
                RegexOptions.Compiled);

            foreach (string file in Directory.GetFiles(Path.Combine(root, "Source"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (Match match in keyPattern.Matches(text)) used.Add(match.Groups[1].Value);
                foreach (Match match in keyCallPattern.Matches(text))
                {
                    if (match.Groups[2].Value.Trim().Length > 0) usedWithArgs.Add(match.Groups[1].Value);
                }
            }

            // Keys referenced indirectly through mapping helpers are still literals, so the
            // pattern above covers them; assert a sane minimum to catch a broken scan.
            TestAssert.Check(used.Count > 80, "key scan found a plausible number of keys: " + used.Count);

            var missing = used.Where(u => !defined.Contains(u)).OrderBy(u => u).ToList();
            TestAssert.Check(missing.Count == 0,
                "all referenced keys are defined; missing: " + string.Join(", ", missing));

            // Format placeholders must match the number of arguments the code passes. This
            // catches the common "translated string lost a {0}" regression.
            var keyedValues = keyed.Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
            var argumentCounts = new Dictionary<string, int>();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Source"), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (KeyValuePair<string, string> call in ExtractTranslateCalls(text))
                {
                    string args = call.Value.Trim();
                    if (args.Length == 0) continue;
                    int count = CountTopLevelArguments(args);
                    int existing;
                    if (!argumentCounts.TryGetValue(call.Key, out existing) || count > existing)
                    {
                        argumentCounts[call.Key] = count;
                    }
                }
            }

            foreach (KeyValuePair<string, int> entry in argumentCounts)
            {
                string value;
                if (!keyedValues.TryGetValue(entry.Key, out value)) continue;
                int placeholders = CountPlaceholders(value);
                TestAssert.Check(placeholders >= entry.Value,
                    "key " + entry.Key + " has " + placeholders + " placeholder(s) but is called with " +
                    entry.Value + " argument(s)");
            }

            // Defs and About metadata must be valid XML with the required elements.
            string defsPath = Path.Combine(root, "Defs", "MainButtonDefs", "PowerGridManager.xml");
            TestAssert.Check(File.Exists(defsPath), "main button def exists");
            XDocument defs = XDocument.Load(defsPath);
            XElement buttonDef = defs.Root.Elements("MainButtonDef").FirstOrDefault();
            TestAssert.NotNull(buttonDef, "MainButtonDef present");
            TestAssert.Equal(buttonDef.Element("defName").Value, "PowerGridManager", "button defName");
            TestAssert.Check(!buttonDef.Element("label").Value.StartsWith("PGM_", StringComparison.Ordinal),
                "def label is display text (defs are translated through DefInjected, not Keyed)");
            TestAssert.Equal(buttonDef.Element("tabWindowClass").Value,
                "PowerGridManager.MainTabWindow_PowerGridManager", "tab window class wiring");

            string aboutPath = Path.Combine(root, "About", "About.xml");
            TestAssert.Check(File.Exists(aboutPath), "About.xml exists");
            XDocument about = XDocument.Load(aboutPath);
            TestAssert.NotNull(about.Root.Element("packageId"), "packageId present");
            var versions = about.Root.Element("supportedVersions").Elements("li").Select(e => e.Value).ToList();
            TestAssert.Check(versions.Contains("1.6"), "1.6 advertised");

            // The icon referenced by the def must exist on disk.
            string iconPath = buttonDef.Element("iconPath").Value;
            TestAssert.Check(File.Exists(Path.Combine(root, "Textures", iconPath + ".png")), "icon texture exists");

            // The tab window type must actually be constructible in this assembly.
            Type windowType = typeof(MainTabWindow_PowerGridManager);
            TestAssert.Check(windowType.IsSubclassOf(typeof(RimWorld.MainTabWindow)), "window extends MainTabWindow");

            Console.WriteLine("Localization and metadata checks passed: " + (TestAssert.Checks - start));
            return TestAssert.Checks - start;
        }

        /// <summary>
        /// Finds every "<key>".Translate(...) call and returns the raw argument text, using
        /// balanced parentheses so nested calls such as Translate(a.Translate(b)) are not
        /// truncated at the first closing bracket.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> ExtractTranslateCalls(string source)
        {
            const string marker = ".Translate(";
            int index = 0;
            while (index < source.Length)
            {
                int callIndex = source.IndexOf(marker, index, StringComparison.Ordinal);
                if (callIndex < 0) break;

                // Walk backwards over the key literal: "PGM_...".
                int quoteEnd = callIndex - 1;
                if (quoteEnd < 0 || source[quoteEnd] != '"')
                {
                    index = callIndex + marker.Length;
                    continue;
                }
                int quoteStart = source.LastIndexOf('"', quoteEnd - 1);
                if (quoteStart < 0)
                {
                    index = callIndex + marker.Length;
                    continue;
                }
                string key = source.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                if (!key.StartsWith("PGM_", StringComparison.Ordinal))
                {
                    index = callIndex + marker.Length;
                    continue;
                }

                int open = callIndex + marker.Length - 1;
                int depth = 0;
                int position = open;
                bool inString = false;
                bool escaped = false;
                for (; position < source.Length; position++)
                {
                    char c = source[position];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == '"') inString = false;
                        continue;
                    }
                    if (c == '"') inString = true;
                    else if (c == '(') depth++;
                    else if (c == ')')
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                }
                if (position >= source.Length) break;
                string args = source.Substring(open + 1, position - open - 1);
                yield return new KeyValuePair<string, string>(key, args);
                index = position + 1;
            }
        }

        private static int CountPlaceholders(string text)
        {
            return Regex.Matches(text, "\\{[0-9]+\\}").Count;
        }

        /// <summary>Counts comma-separated arguments, ignoring commas inside nested calls.</summary>
        private static int CountTopLevelArguments(string args)
        {
            int depth = 0;
            int count = 1;
            foreach (char c in args)
            {
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0) count++;
            }
            return count;
        }

        private static string FindRepositoryRoot()
        {
            var candidates = new List<string>
            {
                Environment.GetEnvironmentVariable("PGM_REPO_ROOT"),
                AppContext.BaseDirectory
            };
            string cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(cwd)) candidates.Add(cwd);

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                DirectoryInfo directory;
                try
                {
                    directory = new DirectoryInfo(Path.GetFullPath(candidate));
                }
                catch (ArgumentException)
                {
                    continue;
                }
                for (int i = 0; i < 10 && directory != null; i++)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "About", "About.xml"))) return directory.FullName;
                    directory = directory.Parent;
                }
            }
            return null;
        }
    }
}
