#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
VERSION="${VERSION:-0.1.10}"
RUNTIME="${RUNTIME:-linux-x64}"
BUILD_DIR="${BUILD_DIR:-$ROOT/src/CNCSync.App/bin/Release/net10.0/$RUNTIME/publish}"
DIST_DIR="${DIST_DIR:-$ROOT/dist/linux/velopack-$RUNTIME}"
PACK_ID="${PACK_ID:-3DTek.CNCSync}"
MAIN_EXE="${MAIN_EXE:-CNCSync}"
PACK_TITLE="${PACK_TITLE:-CNC Sync}"
PACK_AUTHORS="${PACK_AUTHORS:-3DTek}"
ICON_PATH="${ICON_PATH:-$ROOT/src/CNCSync.App/Assets/cnc-sync-tray.png}"
VPK_BIN="${VPK_BIN:-vpk}"

rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

"$VPK_BIN" '[linux]' pack \
  --packId "$PACK_ID" \
  --packVersion "$VERSION" \
  --packDir "$BUILD_DIR" \
  --mainExe "$MAIN_EXE" \
  --packTitle "$PACK_TITLE" \
  --packAuthors "$PACK_AUTHORS" \
  --outputDir "$DIST_DIR" \
  --runtime "$RUNTIME" \
  --channel "$RUNTIME" \
  --icon "$ICON_PATH"

appimage_file="$(find "$DIST_DIR" -maxdepth 1 -name '*.AppImage' | head -n 1)"
if [[ -n "$appimage_file" ]]; then
  cp "$appimage_file" "$DIST_DIR/cnc-sync-linux-x64-latest.AppImage"
fi

echo "Packaged Velopack Linux release at:"
echo "$DIST_DIR"
