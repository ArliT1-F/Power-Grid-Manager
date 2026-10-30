#!/usr/bin/env python3
"""Dependency-free static checks for Power Grid Manager.

This is NOT a compiler. It verifies XML metadata, localization completeness and
placeholder counts, source hygiene (no reflection, Harmony, DLC namespaces or
background work) and the presence of advertised assets. The C# harness in this
directory is the authoritative logic test; run it with `dotnet run` when a .NET
SDK is available (CI does this). With `--syntax`, this script additionally parses
every C# file with Roslyn through the optional @scalar/csharp-fmt npm package,
which catches syntax errors without a local .NET installation.

Usage:
    python3 Tests/static_checks.py
    python3 Tests/static_checks.py --syntax     # requires Node.js 22+ and npm package
"""
import argparse
import glob
import os
import re
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET

FORBIDDEN = [
    "using System.Linq", ".Where(", ".Select(", ".OrderBy(", ".ToArray()",
    "System.Reflection", "BindingFlags", "GetField(", "GetProperty(",
    "using HarmonyLib", "[HarmonyPatch", "HarmonyLib.",
    "new Thread", "Task.Run", "ThreadPool", "BackgroundWorker",
    "Log.Error", "Log.ErrorOnce", "Log.WarningOnce",
]
DLC_NAMESPACES = [
    "RimWorld.Biotech", "RimWorld.Ideology", "RimWorld.Royalty",
    "RimWorld.Anomaly", "RimWorld.Odyssey",
]


def repo_root():
    directory = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    for _ in range(6):
        if os.path.isfile(os.path.join(directory, "About", "About.xml")):
            return directory
        parent = os.path.dirname(directory)
        if parent == directory:
            break
        directory = parent
    return None


def strip_comments(text):
    """Remove // and /* */ comments so documentation words are not mistaken for code."""
    out = []
    block = line = string = verbatim = False
    i = 0
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ""
        if line:
            if c == "\n":
                line = False
                out.append("\n")
            i += 1
            continue
        if block:
            if c == "*" and n == "/":
                block = False
                i += 2
            else:
                i += 1
            continue
        if string:
            if verbatim and c == '"' and n == '"':
                out.append(c)
                i += 2
                continue
            if c == '"' and (i == 0 or text[i - 1] != "\\"):
                string = False
            out.append(c)
            i += 1
            continue
        if c == "/" and n == "/":
            line = True
            i += 2
            continue
        if c == "/" and n == "*":
            block = True
            i += 2
            continue
        if c == "@" and n == '"':
            verbatim = string = True
            out.append(c)
            i += 1
            continue
        if c == '"':
            verbatim = False
            string = True
        out.append(c)
        i += 1
    return "".join(out)


def translate_calls(source):
    """Yield (key, raw argument text) for "KEY".Translate(...) calls."""
    marker = ".Translate("
    index = 0
    while True:
        call = source.find(marker, index)
        if call < 0:
            return
        quote_end = call - 1
        if quote_end < 0 or source[quote_end] != '"':
            index = call + len(marker)
            continue
        quote_start = source.rfind('"', 0, quote_end)
        key = source[quote_start + 1:quote_end] if quote_start >= 0 else ""
        open_paren = call + len(marker) - 1
        depth = 0
        pos = open_paren
        in_string = escaped = False
        while pos < len(source):
            ch = source[pos]
            if in_string:
                if escaped:
                    escaped = False
                elif ch == "\\":
                    escaped = True
                elif ch == '"':
                    in_string = False
            elif ch == '"':
                in_string = True
            elif ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0:
                    break
            pos += 1
        if pos >= len(source):
            return
        if key.startswith("PGM_"):
            yield key, source[open_paren + 1:pos]
        index = pos + 1


def count_arguments(text):
    if not text.strip():
        return 0
    depth = count = 1
    for ch in text:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            count += 1
    return count


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--syntax", action="store_true",
                        help="also parse every C# file with Roslyn via @scalar/csharp-fmt")
    args = parser.parse_args()
    root = repo_root()
    errors = []
    checks = 0

    def check(condition, message):
        nonlocal checks
        checks += 1
        if not condition:
            errors.append(message)

    if root is None:
        print("FAIL: could not locate the repository root")
        return 1

    # XML metadata must parse.
    xml_files = []
    for folder in ("About", "Defs", "Languages"):
        xml_files += glob.glob(os.path.join(root, folder, "**", "*.xml"), recursive=True)
    check(len(xml_files) >= 3, "expected metadata/keyed XML files")
    for path in xml_files:
        try:
            ET.parse(path)
        except ET.ParseError as exc:
            errors.append("malformed XML %s: %s" % (os.path.relpath(path, root), exc))
    checks += len(xml_files)

    keyed_path = os.path.join(root, "Languages", "English", "Keyed", "PowerGridManager.xml")
    check(os.path.isfile(keyed_path), "English keyed file exists")
    keyed = ET.parse(keyed_path).getroot()
    defined = {}
    for element in keyed:
        check(element.tag not in defined, "duplicate key: " + element.tag)
        defined[element.tag] = element.text or ""

    sources = sorted(glob.glob(os.path.join(root, "Source", "**", "*.cs"), recursive=True))
    check(len(sources) >= 20, "expected production source files")
    used, argument_counts = set(), {}
    for path in sources:
        text = open(path, encoding="utf-8").read()
        name = os.path.relpath(path, root)
        used.update(re.findall(r'"(PGM_[A-Za-z0-9_]+)"', text))
        for key, raw in translate_calls(text):
            count = count_arguments(raw)
            argument_counts[key] = max(argument_counts.get(key, 0), count)
        code = strip_comments(text)
        for pattern in FORBIDDEN:
            check(pattern not in code, "%s contains forbidden pattern %s" % (name, pattern))
        for namespace in DLC_NAMESPACES:
            check(namespace not in code, "%s depends on DLC namespace %s" % (name, namespace))

    missing = sorted(used - set(defined))
    check(not missing, "undefined localization keys: " + ", ".join(missing))
    for key, count in argument_counts.items():
        if key not in defined:
            continue
        placeholders = len(re.findall(r"\{[0-9]+\}", defined[key]))
        check(placeholders >= count,
              "key %s has %d placeholder(s) but is called with %d argument(s)" %
              (key, placeholders, count))

    # Def wiring and texture.
    defs = ET.parse(os.path.join(root, "Defs", "MainButtonDefs", "PowerGridManager.xml")).getroot()
    button = defs.find("MainButtonDef")
    check(button is not None, "MainButtonDef exists")
    if button is not None:
        check(button.findtext("tabWindowClass") == "PowerGridManager.MainTabWindow_PowerGridManager",
              "main tab window class is wired")
        icon = button.findtext("iconPath")
        check(icon is not None and os.path.isfile(os.path.join(root, "Textures", icon + ".png")),
              "main tab icon texture exists")

    about = ET.parse(os.path.join(root, "About", "About.xml")).getroot()
    versions = {e.text for e in about.find("supportedVersions").findall("li")}
    check({"1.5", "1.6", "1.7"}.issubset(versions), "1.5/1.6/1.7 are advertised")
    mod_version = (about.findtext("modVersion") or "").strip()
    check(bool(mod_version), "About.xml declares modVersion")
    project_xml = ET.parse(os.path.join(root, "Source", "PowerGridManager",
                                        "PowerGridManager.csproj")).getroot()
    project_version = (project_xml.findtext("./PropertyGroup/Version") or "").strip()
    check(bool(mod_version) and project_version == mod_version,
          "project Version matches About.xml modVersion")
    check(bool((about.findtext("url") or "").strip()), "About.xml declares repository url")
    check(about.find("modDependencies") is None and about.find("dependencies") is None,
          "no hard dependencies or DLC requirement")

    # LoadFolders.xml must mirror RimWorld's ModLoadFolders.GetIssueList checks.
    load_folders_path = os.path.join(root, "LoadFolders.xml")
    check(os.path.isfile(load_folders_path), "LoadFolders.xml exists")
    if os.path.isfile(load_folders_path):
        lf_root = ET.parse(load_folders_path).getroot()
        check(lf_root.tag == "loadFolders", "LoadFolders.xml root tag is loadFolders")
        prev_ver = None
        seen_versions = set()
        for child in list(lf_root):
            tag = child.tag[1:] if child.tag.lower().startswith("v") else child.tag
            seen_versions.add(tag)
            check(tag in versions, "LoadFolders version %s is supported in About.xml" % tag)
            ver_tuple = tuple(int(p) for p in tag.split("."))
            if prev_ver is not None:
                check(ver_tuple > prev_ver, "LoadFolders version %s is in ascending order" % tag)
            prev_ver = ver_tuple
            entries = [li.text or "" for li in child.findall("li")]
            check(len(entries) >= 2 and "/" in entries and tag in entries,
                  "LoadFolders %s maps both / and %s" % (tag, tag))
            for folder in entries:
                resolved = root if folder in ("/", "\\", "") else os.path.join(root, folder)
                check(os.path.isdir(resolved),
                      "LoadFolders directory exists for %s: %s" % (tag, folder))
        check({"1.5", "1.6", "1.7"}.issubset(seen_versions),
              "LoadFolders.xml defines 1.5, 1.6 and 1.7")

    # Release assets, license, packager, and PNG dimensions.
    license_path = os.path.join(root, "LICENSE")
    check(os.path.isfile(license_path) and "MIT License" in open(license_path, encoding="utf-8").read(),
          "MIT LICENSE file exists")
    for script_name in ("build.sh", "package-release.sh"):
        script_path = os.path.join(root, script_name)
        check(os.path.isfile(script_path) and os.access(script_path, os.X_OK),
              "%s exists and is executable" % script_name)
    for png_rel, expected_size in (
        (os.path.join("About", "ModIcon.png"), None),
        (os.path.join("About", "Preview.png"), (640, 360)),
    ):
        png_path = os.path.join(root, png_rel)
        check(os.path.isfile(png_path), "%s exists" % png_rel)
        if os.path.isfile(png_path):
            header = open(png_path, "rb").read(24)
            is_png = len(header) >= 24 and header[:8] == b"\x89PNG\r\n\x1a\n"
            check(is_png, "%s has a valid PNG signature" % png_rel)
            if is_png and expected_size is not None:
                width, height = struct.unpack(">II", header[16:24])
                check((width, height) == expected_size,
                      "%s dimensions are %dx%d (got %dx%d)" %
                      (png_rel, expected_size[0], expected_size[1], width, height))

    for doc_rel in (
        "README.md",
        "CHANGELOG.md",
        os.path.join("Docs", "ARCHITECTURE.md"),
        os.path.join("Docs", "COMPATIBILITY.md"),
        os.path.join("Docs", "FEATURES-AND-LIMITATIONS.md"),
        os.path.join("Docs", "SCREENSHOTS.md"),
        os.path.join("Docs", "WORKSHOP.md"),
    ):
        check(os.path.isfile(os.path.join(root, doc_rel)), "%s exists" % doc_rel)

    # Every relative Markdown link/image in README.md and Docs/*.md must resolve on disk.
    md_files = [os.path.join(root, "README.md"), os.path.join(root, "CHANGELOG.md")] + sorted(
        glob.glob(os.path.join(root, "Docs", "*.md")))
    link_re = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
    for md_path in md_files:
        if not os.path.isfile(md_path):
            continue
        md_dir = os.path.dirname(md_path)
        md_rel = os.path.relpath(md_path, root)
        md_body = re.sub(r"```.*?```", "", open(md_path, encoding="utf-8").read(), flags=re.S)
        for target in link_re.findall(md_body):
            target = target.strip().split("#", 1)[0]
            if not target or "://" in target or target.startswith("mailto:"):
                continue
            resolved = os.path.normpath(os.path.join(md_dir, target))
            check(os.path.exists(resolved),
                  "%s links to missing path %s" % (md_rel, target))

    project = open(os.path.join(root, "Source", "PowerGridManager",
                                "PowerGridManager.csproj"), encoding="utf-8").read()
    check("PackageReference" not in project, "production project has no external packages")
    check("harmony" not in project.lower(), "production project has no Harmony reference")
    check("Assembly-CSharp.dll" in open(os.path.join(root, "build.sh"), encoding="utf-8").read(),
          "build script references the game assembly")

    # Optional Roslyn syntax check through a WASM build of CSharpier.
    if args.syntax:
        node = """
const { init, formatSync } = require(process.argv[2]);
const fs = require('fs');
(async () => {
  await init();
  let bad = 0;
  for (const f of process.argv.slice(3)) {
    try { formatSync(fs.readFileSync(f, 'utf8'), { printWidth: 120 }); }
    catch (e) { bad++; console.log('SYNTAX FAIL ' + f + ': ' + String(e.message || e).slice(0, 400)); }
  }
  process.exit(bad ? 1 : 0);
})();
"""
        script = os.path.join(root, "Tests", ".syntax-check.cjs")
        with open(script, "w", encoding="utf-8") as handle:
            handle.write(node)
        fmt_path = os.environ.get("PGM_CSHARP_FMT") or os.path.join(
            root, "node_modules", "@scalar", "csharp-fmt")
        try:
            result = subprocess.run(
                ["node", script, fmt_path]
                + sources + sorted(glob.glob(os.path.join(root, "Tests", "**", "*.cs"), recursive=True)),
                cwd=root, capture_output=True, text=True, timeout=300)
            check(result.returncode == 0,
                  "Roslyn syntax check failed: " + (result.stdout + result.stderr)[:800])
        except (OSError, subprocess.TimeoutExpired) as exc:
            errors.append("Roslyn syntax check unavailable: %s" % exc)
        finally:
            if os.path.exists(script):
                os.remove(script)
        checks += 1

    print("Static checks: %d performed, %d failure(s)" % (checks, len(errors)))
    for error in errors:
        print(" - " + error)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
