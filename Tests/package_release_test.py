#!/usr/bin/env python3
"""Exercise release staging with fake DLLs; this does not validate game binaries."""
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
VERSIONS = ("1.5", "1.6", "1.7")

with tempfile.TemporaryDirectory() as temp:
    root = Path(temp) / "repo"
    shutil.copytree(ROOT, root, ignore=shutil.ignore_patterns(
        ".git", "dist", "bin", "obj", "*.dll", "__pycache__"))
    version = ET.parse(root / "About/About.xml").getroot().findtext("modVersion").strip()
    assert version

    # Keep the test deterministic even if the developer has a real local Workshop ID.
    workshop_id = root / "About/PublishedFileId.txt"
    workshop_id.unlink(missing_ok=True)
    staged = root / "dist/PowerGridManager"

    def run(*args):
        return subprocess.run(["sh", str(root / "package-release.sh"), *args],
                              cwd=root, capture_output=True, text=True)

    # A release must fail before staging if SDK assembly and mod versions drift.
    project = root / "Source/PowerGridManager/PowerGridManager.csproj"
    original_project = project.read_text()
    project.write_text(original_project.replace("<Version>" + version + "</Version>",
                                                "<Version>99.0.0</Version>"))
    result = run()
    assert result.returncode != 0 and "project Version matches About.xml modVersion" in result.stdout
    project.write_text(original_project)

    result = run()
    assert result.returncode != 0 and all(v in result.stderr for v in VERSIONS), result.stderr
    dll = root / "1.6/Assemblies/PowerGridManager.dll"
    dll.parent.mkdir(parents=True, exist_ok=True)
    dll.write_bytes(b"test placeholder, not a real assembly")
    result = run()
    assert result.returncode != 0 and "1.5 1.7" in result.stderr, result.stderr

    result = run("--allow-unbuilt")
    assert result.returncode == 0, result.stderr
    assert (staged / "UNBUILT-PACKAGE.txt").exists()
    assert not (staged / "About/PublishedFileId.txt").exists()
    dry_archive = root / "dist" / ("PowerGridManager-" + version + "-unbuilt-layout.zip")
    assert dry_archive.is_file()
    with zipfile.ZipFile(dry_archive) as archive:
        names = set(archive.namelist())
        assert "PowerGridManager/UNBUILT-PACKAGE.txt" in names
        assert "PowerGridManager/About/PublishedFileId.txt" not in names

    for game_version in ("1.5", "1.7"):
        path = root / game_version / "Assemblies/PowerGridManager.dll"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"test placeholder, not a real assembly")
    fake_id = b"1234567890\n"  # Test fixture only, not a real Workshop item ID.
    workshop_id.write_bytes(fake_id)
    result = run()
    assert result.returncode == 0, result.stderr
    assert not (staged / "UNBUILT-PACKAGE.txt").exists()
    assert (staged / "About/PublishedFileId.txt").read_bytes() == fake_id
    for game_version in VERSIONS:
        assert (staged / game_version / "Assemblies/PowerGridManager.dll").exists()
    release_archive = root / "dist" / ("PowerGridManager-" + version + ".zip")
    assert release_archive.is_file()
    with zipfile.ZipFile(release_archive) as archive:
        names = set(archive.namelist())
        assert archive.read("PowerGridManager/About/PublishedFileId.txt") == fake_id
        about = ET.fromstring(archive.read("PowerGridManager/About/About.xml"))
        assert about.findtext("modVersion").strip() == version
        assert "PowerGridManager/UNBUILT-PACKAGE.txt" not in names
        assert not any(name.startswith(("PowerGridManager/Source/", "PowerGridManager/Tests/",
                                        "PowerGridManager/Docs/", "PowerGridManager/.git/"))
                       for name in names)
        for game_version in VERSIONS:
            assert "PowerGridManager/" + game_version + "/Assemblies/PowerGridManager.dll" in names

    # Restaging without an ID must not leave a stale one in the folder or zip.
    workshop_id.unlink()
    result = run()
    assert result.returncode == 0, result.stderr
    assert not (staged / "About/PublishedFileId.txt").exists()
    with zipfile.ZipFile(release_archive) as archive:
        assert "PowerGridManager/About/PublishedFileId.txt" not in archive.namelist()

print("Release packaging tests passed (fake DLLs and Workshop ID only).")
