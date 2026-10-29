#!/usr/bin/env sh
# Assembles a clean, distributable RimWorld mod folder and .zip archive from the
# repository root after PowerGridManager.dll has been built for every
# supported game version via ./build.sh.
#
# Usage:
#   ./package-release.sh                    # requires DLLs for 1.5, 1.6 and 1.7
#   ./package-release.sh --allow-unbuilt    # offline/CI dry-run: verifies layout without game DLLs
#   ./package-release.sh --output-dir PATH  # override output directory (default: ./dist)
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ALLOW_UNBUILT=0
OUT_DIR="$ROOT/dist"

while [ $# -gt 0 ]; do
  case "$1" in
    --allow-unbuilt)
      ALLOW_UNBUILT=1
      shift
      ;;
    --output-dir)
      if [ $# -lt 2 ]; then
        echo "Missing directory argument for --output-dir" >&2
        exit 2
      fi
      OUT_DIR="$2"
      shift 2
      ;;
    -h|--help)
      sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

# 1. Run static checks first when python3 is available.
if command -v python3 >/dev/null 2>&1; then
  python3 "$ROOT/Tests/static_checks.py"
fi

# 2. Verify required mod metadata and assets exist.
for required in \
  "About/About.xml" \
  "About/ModIcon.png" \
  "About/Preview.png" \
  "LoadFolders.xml" \
  "Defs/MainButtonDefs/PowerGridManager.xml" \
  "Languages/English/Keyed/PowerGridManager.xml" \
  "Textures/UI/Icons/PowerGridManager.png" \
  "LICENSE" \
  "README.md" \
  "CHANGELOG.md"
do
  if [ ! -f "$ROOT/$required" ]; then
    echo "Missing required release file: $required" >&2
    exit 2
  fi
done

# 3. Extract modVersion from About/About.xml.
MOD_VERSION=$(sed -n 's:.*<modVersion>\([^<]*\)</modVersion>.*:\1:p' "$ROOT/About/About.xml" | head -n 1 | tr -d '\r ')
MOD_VERSION=${MOD_VERSION:-dev}

# 4. Discover compiled version-scoped assemblies.
BUILT_VERSIONS=""
MISSING_VERSIONS=""
for ver in 1.5 1.6 1.7; do
  if [ -f "$ROOT/$ver/Assemblies/PowerGridManager.dll" ]; then
    BUILT_VERSIONS="${BUILT_VERSIONS:+$BUILT_VERSIONS }$ver"
  else
    MISSING_VERSIONS="${MISSING_VERSIONS:+$MISSING_VERSIONS }$ver"
  fi
done

if [ -n "$MISSING_VERSIONS" ] && [ "$ALLOW_UNBUILT" -ne 1 ]; then
  echo "Missing PowerGridManager.dll for advertised RimWorld version(s): $MISSING_VERSIONS" >&2
  echo "Build each version against its own game assemblies with ./build.sh before releasing." >&2
  echo "Use --allow-unbuilt only for a non-distributable layout dry run." >&2
  exit 2
fi

# 5. Stage clean mod directory without Source/, Tests/, Docs/, .git/, or .pdb files.
STAGE="$OUT_DIR/PowerGridManager"
rm -rf "$STAGE"
mkdir -p \
  "$STAGE/About" \
  "$STAGE/1.5/Assemblies" \
  "$STAGE/1.6/Assemblies" \
  "$STAGE/1.7/Assemblies"

cp "$ROOT/About/About.xml" "$ROOT/About/ModIcon.png" "$ROOT/About/Preview.png" "$STAGE/About/"
cp "$ROOT/LoadFolders.xml" "$ROOT/LICENSE" "$ROOT/README.md" "$ROOT/CHANGELOG.md" "$STAGE/"
cp -R "$ROOT/Defs" "$ROOT/Languages" "$ROOT/Textures" "$STAGE/"

for ver in 1.5 1.6 1.7; do
  if [ -f "$ROOT/$ver/Assemblies/PowerGridManager.dll" ]; then
    cp "$ROOT/$ver/Assemblies/PowerGridManager.dll" "$STAGE/$ver/Assemblies/"
  fi
done

ARCHIVE_SUFFIX="$MOD_VERSION"
if [ "$ALLOW_UNBUILT" -eq 1 ]; then
  ARCHIVE_SUFFIX="$MOD_VERSION-unbuilt-layout"
  cat > "$STAGE/UNBUILT-PACKAGE.txt" <<'EOF'
WARNING: This archive was staged with --allow-unbuilt for layout verification only.
It may be missing compiled PowerGridManager.dll assemblies and must NOT be distributed.
Build all supported versions against their respective RimWorld installations using ./build.sh.
EOF
fi

ARCHIVE="$OUT_DIR/PowerGridManager-$ARCHIVE_SUFFIX.zip"
rm -f "$ARCHIVE"

if command -v python3 >/dev/null 2>&1; then
  python3 - "$OUT_DIR" "$ARCHIVE" <<'PY'
import os
import sys
import zipfile

base_dir, archive_path = sys.argv[1], sys.argv[2]
mod_dir = os.path.join(base_dir, "PowerGridManager")
with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    # Include explicit directory entries so empty 1.X/Assemblies folders exist on unzip,
    # keeping LoadFolders.xml warning-free even when only a subset of versions was built.
    for current_root, dirs, files in os.walk(mod_dir):
        dirs.sort()
        files.sort()
        rel_dir = os.path.relpath(current_root, base_dir).replace(os.sep, "/") + "/"
        zinfo = zipfile.ZipInfo(rel_dir)
        zinfo.external_attr = (0o40755 << 16) | 0x10
        zf.writestr(zinfo, b"")
        for filename in files:
            full_path = os.path.join(current_root, filename)
            rel_path = os.path.relpath(full_path, base_dir).replace(os.sep, "/")
            zf.write(full_path, rel_path)
PY
elif command -v zip >/dev/null 2>&1; then
  (cd "$OUT_DIR" && zip -qr "$ARCHIVE" PowerGridManager)
else
  echo "Neither python3 nor zip is available to create $ARCHIVE; staged directory is ready at $STAGE" >&2
  exit 2
fi

echo "Staged mod directory: $STAGE"
echo "Archive created:      $ARCHIVE"
echo "Included assemblies:  ${BUILT_VERSIONS:-none}"
if [ "$ALLOW_UNBUILT" -eq 1 ]; then
  echo "WARNING: layout dry run only; do not distribute or upload this package."
fi
