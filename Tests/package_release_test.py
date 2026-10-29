#!/usr/bin/env python3
"""Exercise release gating with fake DLLs; this does not validate game binaries."""
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSIONS = ("1.5", "1.6", "1.7")

with tempfile.TemporaryDirectory() as temp:
    root = Path(temp) / "repo"
    shutil.copytree(ROOT, root, ignore=shutil.ignore_patterns(
        ".git", "dist", "bin", "obj", "*.dll", "__pycache__"))

    def run(*args):
        return subprocess.run(["sh", str(root / "package-release.sh"), *args],
                              cwd=root, capture_output=True, text=True)

    result = run()
    assert result.returncode != 0 and all(v in result.stderr for v in VERSIONS), result.stderr
    dll = root / "1.6/Assemblies/PowerGridManager.dll"
    dll.parent.mkdir(parents=True, exist_ok=True)
    dll.write_bytes(b"test placeholder, not a real assembly")
    result = run()
    assert result.returncode != 0 and "1.5 1.7" in result.stderr, result.stderr
    result = run("--allow-unbuilt")
    assert result.returncode == 0, result.stderr
    assert (root / "dist/PowerGridManager/UNBUILT-PACKAGE.txt").exists()
    assert list((root / "dist").glob("*-unbuilt-layout.zip"))
    for version in ("1.5", "1.7"):
        path = root / version / "Assemblies/PowerGridManager.dll"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"test placeholder, not a real assembly")
    result = run()
    assert result.returncode == 0, result.stderr
    assert not (root / "dist/PowerGridManager/UNBUILT-PACKAGE.txt").exists()
    for version in VERSIONS:
        assert (root / "dist/PowerGridManager" / version / "Assemblies/PowerGridManager.dll").exists()
print("Release packaging gate tests passed (fake DLLs only).")
