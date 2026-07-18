#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-1.0.83}"
RUNTIME="${RUNTIME:-osx-arm64}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CNCSync.App/bin/Release/net10.0/$RUNTIME/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/macos/velopack-$RUNTIME}"
PACK_ID="${PACK_ID:-3DTek.ProCutSuiteDesktop}"
MAIN_EXE="${MAIN_EXE:-CNCSync}"
PACK_TITLE="${PACK_TITLE:-ProCut Suite Desktop}"
PACK_AUTHORS="${PACK_AUTHORS:-3DTek}"
PLIST_PATH="${PLIST_PATH:-$ROOT/packaging/macos/Info.plist}"
VPK_BIN="${VPK_BIN:-vpk}"
BRIDGE_SOURCE="$ROOT/src/CNCSync.App/MacBridge/bridge.swift"
BRIDGE_NAME="libcncsync-login-item-bridge.dylib"
ICON_SCRIPT="$ROOT/packaging/macos/build-icon.sh"
ICON_FILE="$ROOT/packaging/macos/procut-suite.icns"

case "$RUNTIME" in
  osx-arm64)
    PACKAGE_SUFFIX="macos-arm64"
    ;;
  osx-x64)
    PACKAGE_SUFFIX="macos-x64"
    ;;
  *)
    echo "Unsupported macOS runtime: $RUNTIME" >&2
    exit 1
    ;;
esac

rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

xcrun swiftc -emit-library -framework Foundation -framework ServiceManagement "$BRIDGE_SOURCE" -o "$BUILD_DIR/$BRIDGE_NAME"
zsh "$ICON_SCRIPT" >/dev/null

"$VPK_BIN" '[osx]' pack \
  --packId "$PACK_ID" \
  --packVersion "$VERSION" \
  --packDir "$BUILD_DIR" \
  --mainExe "$MAIN_EXE" \
  --packTitle "$PACK_TITLE" \
  --packAuthors "$PACK_AUTHORS" \
  --outputDir "$DIST_DIR" \
  --runtime "$RUNTIME" \
  --channel "$RUNTIME" \
  --icon "$ICON_FILE" \
  --plist "$PLIST_PATH"

portable_file="$(find "$DIST_DIR" -maxdepth 1 -name '*Portable.zip' | head -n 1)"
setup_file="$(find "$DIST_DIR" -maxdepth 1 -name '*Setup.pkg' | head -n 1)"

if [[ -n "$portable_file" ]]; then
  cp "$portable_file" "$DIST_DIR/procut-suite-desktop-$PACKAGE_SUFFIX-latest.zip"
fi

if [[ -n "$setup_file" ]]; then
  cp "$setup_file" "$DIST_DIR/procut-suite-desktop-$PACKAGE_SUFFIX-latest.pkg"
fi

echo "Packaged Velopack macOS release at:"
echo "$DIST_DIR"
