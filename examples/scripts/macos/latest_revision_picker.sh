#!/usr/bin/env bash
set -euo pipefail

SOURCE_PATH="${1:?source path required}"
OUTPUT_PATH="${2:?output path required}"

if [[ ! -d "$SOURCE_PATH" ]]; then
  echo "latest_revision_picker expects a folder source path." >&2
  exit 1
fi

mkdir -p "$OUTPUT_PATH"

python3 - "$SOURCE_PATH" "$OUTPUT_PATH" <<'PY'
import pathlib, re, shutil, sys

source_root = pathlib.Path(sys.argv[1])
output_root = pathlib.Path(sys.argv[2])
revision_pattern = re.compile(r"R(\d{2})", re.IGNORECASE)

files = [path for path in source_root.rglob("*") if path.is_file()]
revisioned = [path for path in files if revision_pattern.search(path.name)]
latest = None
if revisioned:
    latest = max(int(revision_pattern.search(path.name).group(1)) for path in revisioned)

for path in files:
    match = revision_pattern.search(path.name)
    if latest is not None and match and int(match.group(1)) != latest:
        continue
    rel = path.relative_to(source_root)
    dst = output_root / rel
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, dst)
PY

echo "OUTPUT_PATH=$OUTPUT_PATH"
