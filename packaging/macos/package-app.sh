#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-0.1.3}"
RUNTIME="${RUNTIME:-osx-arm64}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CBWSSSync.App/bin/Release/net10.0/$RUNTIME/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/macos}"
APP_DIR="$DIST_DIR/CNC Sync.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

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

cp "$ROOT/packaging/macos/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$BUILD_DIR"/. "$MACOS_DIR"/
chmod +x "$MACOS_DIR/CBWSSSync"

rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"
cp "$ZIP_PATH" "$LATEST_ZIP_PATH"

echo "Packaged app bundle at:"
echo "$APP_DIR"
echo "Packaged zip at:"
echo "$ZIP_PATH"
echo "Packaged stable zip at:"
echo "$LATEST_ZIP_PATH"
