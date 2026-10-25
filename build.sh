#!/usr/bin/env sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
VERSION=${RIMWORLD_GAME_VERSION:-1.6}
case "$VERSION" in
  1.5|1.6)
    if [ -z "${RIMWORLD_MANAGED_DIR:-}" ]; then
      echo "Set RIMWORLD_MANAGED_DIR to the RimWorld $VERSION installation's Managed directory." >&2
      echo 'Example: RIMWORLD_GAME_VERSION=1.5 RIMWORLD_MANAGED_DIR="/games/RimWorld-1.5/RimWorldWin64_Data/Managed" ./build.sh' >&2
      exit 2
    fi
    if [ ! -f "$RIMWORLD_MANAGED_DIR/Assembly-CSharp.dll" ]; then
      echo "Assembly-CSharp.dll not found under: $RIMWORLD_MANAGED_DIR" >&2
      exit 2
    fi
    set -- -p:RimWorldManagedDir="$RIMWORLD_MANAGED_DIR"
    ;;
  1.7)
    if [ -z "${RIMWORLD_DIR:-}" ]; then
      echo "Set RIMWORLD_DIR to the RimWorld 1.7 installation root." >&2
      echo 'Example: RIMWORLD_GAME_VERSION=1.7 RIMWORLD_DIR="/games/RimWorld-1.7" ./build.sh' >&2
      exit 2
    fi
    if [ ! -f "$RIMWORLD_DIR/Assembly-CSharp.dll" ]; then
      echo "RimWorld 1.7 Assembly-CSharp.dll not found under: $RIMWORLD_DIR" >&2
      exit 2
    fi
    set -- -p:RimWorldDir="$RIMWORLD_DIR"
    ;;
  *)
    echo "Unsupported RIMWORLD_GAME_VERSION '$VERSION'; select 1.5, 1.6, or 1.7." >&2
    exit 2
    ;;
esac

if command -v dotnet >/dev/null 2>&1; then
  dotnet build "$ROOT/Source/PowerGridManager/PowerGridManager.csproj" --configuration Release \
    -p:RimWorldGameVersion="$VERSION" "$@"
elif command -v msbuild >/dev/null 2>&1; then
  msbuild "$ROOT/Source/PowerGridManager/PowerGridManager.csproj" /restore /p:Configuration=Release \
    "/p:RimWorldGameVersion=$VERSION" "$@"
else
  echo "Neither dotnet nor msbuild is installed. Install the target runtime's .NET SDK/MSBuild toolchain and retry." >&2
  exit 2
fi
