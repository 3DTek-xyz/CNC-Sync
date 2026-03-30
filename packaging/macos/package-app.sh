#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-0.1.10}"
RUNTIME="${RUNTIME:-osx-arm64}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CNCSync.App/bin/Release/net10.0/$RUNTIME/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/macos}"
APP_DIR="$DIST_DIR/CNC Sync.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
BRIDGE_SOURCE="$ROOT/src/CNCSync.App/MacBridge/bridge.swift"
BRIDGE_NAME="libcncsync-login-item-bridge.dylib"
ICON_SCRIPT="$ROOT/packaging/macos/build-icon.sh"
ICON_FILE="$ROOT/packaging/macos/cnc-sync.icns"

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

ZIP_PATH="$DIST_DIR/cnc-sync-$PACKAGE_SUFFIX-v$VERSION.zip"
LATEST_ZIP_PATH="$DIST_DIR/cnc-sync-$PACKAGE_SUFFIX-latest.zip"

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
mkdir -p "$DIST_DIR"

xcrun swiftc -emit-library -framework Foundation -framework ServiceManagement "$BRIDGE_SOURCE" -o "$BUILD_DIR/$BRIDGE_NAME"
"$ICON_SCRIPT" >/dev/null

cp "$ROOT/packaging/macos/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$BUILD_DIR"/. "$MACOS_DIR"/
cp "$ICON_FILE" "$RESOURCES_DIR/cnc-sync.icns"
chmod +x "$MACOS_DIR/CNCSync"

rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"
cp "$ZIP_PATH" "$LATEST_ZIP_PATH"

echo "Packaged app bundle at:"
echo "$APP_DIR"
echo "Packaged zip at:"
echo "$ZIP_PATH"
echo "Packaged stable zip at:"
echo "$LATEST_ZIP_PATH"
