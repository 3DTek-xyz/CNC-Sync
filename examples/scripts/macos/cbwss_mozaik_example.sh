#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_SCRIPT="$SCRIPT_DIR/../shared/cbwss_mozaik_example.py"

exec python3 "$PYTHON_SCRIPT" "$@"
