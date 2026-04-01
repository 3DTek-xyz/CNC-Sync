#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="${ROOT:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
SOURCE_PNG="${SOURCE_PNG:-$ROOT/design/icon-options/target-ring-classic.svg.png}"
OUTPUT_ICNS="${OUTPUT_ICNS:-$ROOT/packaging/macos/cnc-sync.icns}"

if [[ ! -f "$SOURCE_PNG" ]]; then
  echo "Missing macOS icon source: $SOURCE_PNG" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUTPUT_ICNS")"
sips -s format icns "$SOURCE_PNG" --out "$OUTPUT_ICNS" >/dev/null
echo "$OUTPUT_ICNS"
