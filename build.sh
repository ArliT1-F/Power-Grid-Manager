#!/usr/bin/env sh
# Builds PowerGridManager.dll for a specific RimWorld installation.
#
#   RimWorld 1.5 / 1.6  (bundled Mono, net472)
#     RIMWORLD_MANAGED_DIR="/path/to/RimWorld/RimWorldLinux_Data/Managed" ./build.sh
#
#   RimWorld 1.7  (.NET 10, Waylith)
#     RIMWORLD_DIR="/path/to/RimWorld-1.7" ./build.sh
#
# The game version is read from Version.txt next to the Managed directory when
# RIMWORLD_GAME_VERSION is not set; set it explicitly to override.
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
VERSION=${RIMWORLD_GAME_VERSION:-}

# --- Resolve the game version ------------------------------------------------
version_from_file() {
  # Version.txt looks like "1.6.4518 rev123 linux"; keep major.minor.
  head -n 1 "$1" 2>/dev/null | tr -d '\r' | cut -d. -f1,2
}

if [ -z "$VERSION" ]; then
  if [ -n "${RIMWORLD_MANAGED_DIR:-}" ] && [ -f "$RIMWORLD_MANAGED_DIR/../Version.txt" ]; then
    VERSION=$(version_from_file "$RIMWORLD_MANAGED_DIR/../Version.txt")
  elif [ -n "${RIMWORLD_DIR:-}" ] && [ -f "$RIMWORLD_DIR/Version.txt" ]; then
    VERSION=$(version_from_file "$RIMWORLD_DIR/Version.txt")
  fi
fi
VERSION=${VERSION:-1.6}

# --- Validate arguments for that version -------------------------------------
case "$VERSION" in
  1.5|1.6)
    if [ -z "${RIMWORLD_MANAGED_DIR:-}" ]; then
      echo "Set RIMWORLD_MANAGED_DIR to the RimWorld $VERSION installation's Managed directory." >&2
      echo 'Example: RIMWORLD_MANAGED_DIR="/games/RimWorld/RimWorldLinux_Data/Managed" ./build.sh' >&2
      exit 2
    fi
    for f in Assembly-CSharp.dll UnityEngine.CoreModule.dll UnityEngine.IMGUIModule.dll UnityEngine.TextRenderingModule.dll; do
      if [ ! -f "$RIMWORLD_MANAGED_DIR/$f" ]; then
        echo "$f not found under: $RIMWORLD_MANAGED_DIR" >&2
        exit 2
      fi
    done
    set -- -p:RimWorldManagedDir="$RIMWORLD_MANAGED_DIR"
    ;;
  1.7)
    if [ -z "${RIMWORLD_DIR:-}" ]; then
      echo "Set RIMWORLD_DIR to the RimWorld 1.7 installation root." >&2
      echo 'Example: RIMWORLD_DIR="/games/RimWorld-1.7" ./build.sh' >&2
      exit 2
    fi
    if [ ! -f "$RIMWORLD_DIR/Assembly-CSharp.dll" ]; then
      echo "RimWorld 1.7 Assembly-CSharp.dll not found under: $RIMWORLD_DIR" >&2
      exit 2
    fi
    set -- -p:RimWorldDir="$RIMWORLD_DIR"
    ;;
  *)
    echo "Unsupported RimWorld version '$VERSION'; expected 1.5, 1.6 or 1.7." >&2
    echo "Set RIMWORLD_GAME_VERSION explicitly to override the detected version." >&2
    exit 2
    ;;
esac

# --- Build -------------------------------------------------------------------
echo "Building for RimWorld $VERSION"

if command -v dotnet >/dev/null 2>&1; then
  dotnet build "$ROOT/Source/PowerGridManager/PowerGridManager.csproj" --configuration Release \
    -p:RimWorldGameVersion="$VERSION" "$@"
elif command -v msbuild >/dev/null 2>&1; then
  msbuild "$ROOT/Source/PowerGridManager/PowerGridManager.csproj" /restore /p:Configuration=Release \
    "/p:RimWorldGameVersion=$VERSION" "$@"
elif command -v mcs >/dev/null 2>&1 || command -v csc >/dev/null 2>&1; then
  if [ "$VERSION" = "1.7" ]; then
    echo "RimWorld 1.7 requires a .NET 10 SDK build; the Mono compiler cannot target its runtime." >&2
    exit 2
  fi
  echo "No dotnet/msbuild found; falling back to the Mono C# compiler." >&2
  COMPILER=$(command -v mcs || command -v csc)
  OUT="$ROOT/$VERSION/Assemblies"
  mkdir -p "$OUT"

  # The SDK generates assembly version attributes from the project Version. The
  # standalone compiler needs the same attributes explicitly, using About.xml as
  # the release-version source (static_checks.py verifies the project matches it).
  MOD_VERSION=$(sed -n 's:.*<modVersion>\([^<]*\)</modVersion>.*:\1:p' "$ROOT/About/About.xml" | head -n 1 | tr -d '\r ')
  case "$MOD_VERSION" in
    ''|*[!0-9A-Za-z.+-]*) echo "Invalid modVersion in About/About.xml." >&2; exit 2 ;;
  esac
  NUMERIC_VERSION=${MOD_VERSION%%[-+]*}
  if ! printf '%s\n' "$NUMERIC_VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "modVersion must start with major.minor.patch for the Mono build." >&2
    exit 2
  fi
  ASSEMBLY_INFO_DIR=$(mktemp -d "${TMPDIR:-/tmp}/pgm-assembly.XXXXXX")
  trap 'rm -rf "$ASSEMBLY_INFO_DIR"' EXIT
  cat > "$ASSEMBLY_INFO_DIR/AssemblyInfo.cs" <<EOF
[assembly: System.Reflection.AssemblyVersion("$NUMERIC_VERSION.0")]
[assembly: System.Reflection.AssemblyFileVersion("$NUMERIC_VERSION.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$MOD_VERSION")]
EOF
  # shellcheck disable=SC2046
  "$COMPILER" -target:library -langversion:latest -nologo -out:"$OUT/PowerGridManager.dll" \
    -r:"$RIMWORLD_MANAGED_DIR/Assembly-CSharp.dll" \
    -r:"$RIMWORLD_MANAGED_DIR/UnityEngine.CoreModule.dll" \
    -r:"$RIMWORLD_MANAGED_DIR/UnityEngine.IMGUIModule.dll" \
    -r:"$RIMWORLD_MANAGED_DIR/UnityEngine.TextRenderingModule.dll" \
    "$ASSEMBLY_INFO_DIR/AssemblyInfo.cs" \
    $(find "$ROOT/Source/PowerGridManager" \( -name bin -o -name obj \) -type d -prune -o -type f -name '*.cs' -print)
else
  echo "No C# compiler found. Install the .NET SDK (dotnet) or Mono (mono-devel/msbuild), then retry." >&2
  exit 2
fi

echo
echo "Output: $ROOT/$VERSION/Assemblies/PowerGridManager.dll"
echo "To test, make this repository visible to the game, for example:"
echo "  ln -s \"$ROOT\" \"<RimWorld install>/Mods/PowerGridManager\""
