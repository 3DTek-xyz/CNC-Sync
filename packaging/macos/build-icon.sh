#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
SOURCE_PNG="${SOURCE_PNG:-$ROOT/src/CNCSync.App/Assets/procut-suite-tray.png}"
OUTPUT_ICNS="${OUTPUT_ICNS:-$ROOT/packaging/macos/procut-suite.icns}"

if [[ -f "$OUTPUT_ICNS" ]]; then
  echo "$OUTPUT_ICNS"
  exit 0
fi

if [[ ! -f "$SOURCE_PNG" ]]; then
  echo "Missing macOS icon source: $SOURCE_PNG" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUTPUT_ICNS")"
sips -s format icns "$SOURCE_PNG" --out "$OUTPUT_ICNS" >/dev/null
echo "$OUTPUT_ICNS"
