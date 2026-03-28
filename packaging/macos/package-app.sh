#!/bin/zsh
set -euo pipefail

ROOT="/Users/benharper/Coding/CBWSS-Sync"
BUILD_DIR="$ROOT/src/CBWSSSync.App/bin/Release/net9.0/osx-arm64/publish"
APP_DIR="$ROOT/dist/macos/CBWSS Sync.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

cp "$ROOT/packaging/macos/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$BUILD_DIR"/. "$MACOS_DIR"/
chmod +x "$MACOS_DIR/CBWSSSync"

echo "Packaged app bundle at:"
echo "$APP_DIR"
