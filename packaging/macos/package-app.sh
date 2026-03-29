#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-0.1.0}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CBWSSSync.App/bin/Release/net10.0/osx-arm64/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/macos}"
APP_DIR="$DIST_DIR/CNC Sync.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
ZIP_PATH="$DIST_DIR/cnc-sync-macos-arm64-v$VERSION.zip"

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"
mkdir -p "$DIST_DIR"

cp "$ROOT/packaging/macos/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$BUILD_DIR"/. "$MACOS_DIR"/
chmod +x "$MACOS_DIR/CBWSSSync"

rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"

echo "Packaged app bundle at:"
echo "$APP_DIR"
echo "Packaged zip at:"
echo "$ZIP_PATH"
