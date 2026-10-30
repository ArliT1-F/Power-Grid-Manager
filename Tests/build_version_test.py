#!/usr/bin/env python3
"""Check standalone compiler version arguments without building a game DLL."""
import os
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

with tempfile.TemporaryDirectory() as temp:
    temp = Path(temp)
    root = temp / "repo"
    shutil.copytree(ROOT, root, ignore=shutil.ignore_patterns(
        ".git", "dist", "bin", "obj", "*.dll", "__pycache__"))
    version = ET.parse(root / "About/About.xml").getroot().findtext("modVersion").strip()
    numeric = version.split("-", 1)[0].split("+", 1)[0]
    fake_bin = temp / "bin"
    fake_bin.mkdir()
    # A restricted PATH exposes shell utilities and our fake compiler, but not
    # a real dotnet/msbuild, so the Mono fallback is exercised consistently in CI.
    for utility in ("dirname", "head", "tr", "cut", "mkdir", "sed", "grep",
                    "mktemp", "cat", "find", "rm"):
        executable = shutil.which(utility)
        assert executable, "required test utility not found: " + utility
        (fake_bin / utility).symlink_to(executable)
    fake_mcs = fake_bin / "mcs"
    fake_mcs.write_text('''#!/bin/sh
for arg in "$@"; do
  printf '%s\\n' "$arg" >> "$CAPTURED_ARGS"
  case "$arg" in
    */AssemblyInfo.cs)
      cat "$arg" > "$CAPTURED_ASSEMBLY_INFO"
      printf '%s\\n' "$arg" > "$CAPTURED_ASSEMBLY_PATH"
      ;;
  esac
done
''')
    fake_mcs.chmod(0o755)
    # An earlier SDK build can leave generated C# under obj; it must not be
    # recompiled alongside the fallback's own generated assembly attributes.
    obj = root / "Source/PowerGridManager/obj"
    obj.mkdir()
    (obj / "StaleAssemblyInfo.cs").write_text("// should not reach the compiler\n")
    managed = temp / "Managed"
    managed.mkdir()
    for name in ("Assembly-CSharp.dll", "UnityEngine.CoreModule.dll",
                 "UnityEngine.IMGUIModule.dll", "UnityEngine.TextRenderingModule.dll"):
        (managed / name).write_bytes(b"placeholder, not a game assembly")
    captured = temp / "assembly-info.txt"
    captured_path = temp / "assembly-info-path.txt"
    captured_args = temp / "compiler-args.txt"
    env = os.environ.copy()
    env.update({"PATH": str(fake_bin), "TMPDIR": str(temp), "RIMWORLD_GAME_VERSION": "1.6",
                "RIMWORLD_MANAGED_DIR": str(managed), "CAPTURED_ASSEMBLY_INFO": str(captured),
                "CAPTURED_ASSEMBLY_PATH": str(captured_path), "CAPTURED_ARGS": str(captured_args)})
    env.pop("RIMWORLD_DIR", None)

    def run():
        return subprocess.run(["/bin/sh", str(root / "build.sh")], cwd=root, env=env,
                              capture_output=True, text=True)

    result = run()
    assert result.returncode == 0, result.stderr
    info = captured.read_text()
    assert '[assembly: System.Reflection.AssemblyVersion("' + numeric + '.0")]' in info
    assert '[assembly: System.Reflection.AssemblyFileVersion("' + numeric + '.0")]' in info
    assert '[assembly: System.Reflection.AssemblyInformationalVersion("' + version + '")]' in info
    assert not Path(captured_path.read_text().strip()).exists(), "temporary assembly info was not removed"
    args = captured_args.read_text()
    assert "PowerHeatmapPanel.cs" in args
    assert "StaleAssemblyInfo.cs" not in args

    # A Mono fallback cannot build the .NET 10 RimWorld 1.7 target.
    captured.unlink()
    env.update({"RIMWORLD_GAME_VERSION": "1.7", "RIMWORLD_DIR": str(managed)})
    result = run()
    assert result.returncode != 0 and ".NET 10 SDK" in result.stderr, result.stderr
    assert not captured.exists()

    # Refuse unsafe/invalid version text before generating C# compiler input.
    env["RIMWORLD_GAME_VERSION"] = "1.6"
    about_path = root / "About/About.xml"
    about = about_path.read_text()
    about_path.write_text(about.replace("<modVersion>" + version + "</modVersion>",
                                        '<modVersion>1.0.1"invalid</modVersion>'))
    result = run()
    assert result.returncode != 0 and "Invalid modVersion" in result.stderr, result.stderr
    assert not captured.exists()

print("Standalone compiler version tests passed (fake compiler and game assemblies only).")
