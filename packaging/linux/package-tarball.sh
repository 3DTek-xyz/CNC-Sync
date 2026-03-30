#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-0.1.8}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CNCSync.App/bin/Release/net10.0/linux-x64/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/linux}"
PACKAGE_DIR="$DIST_DIR/cnc-sync-linux-x64-v$VERSION"
TARBALL_PATH="$DIST_DIR/cnc-sync-linux-x64-v$VERSION.tar.gz"
LATEST_TARBALL_PATH="$DIST_DIR/cnc-sync-linux-x64-latest.tar.gz"

rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR" "$DIST_DIR"
cp -R "$BUILD_DIR"/. "$PACKAGE_DIR"/

rm -f "$TARBALL_PATH"
tar -C "$DIST_DIR" -czf "$TARBALL_PATH" "$(basename "$PACKAGE_DIR")"
cp "$TARBALL_PATH" "$LATEST_TARBALL_PATH"

echo "Packaged tarball at:"
echo "$TARBALL_PATH"
echo "Packaged stable tarball at:"
echo "$LATEST_TARBALL_PATH"
