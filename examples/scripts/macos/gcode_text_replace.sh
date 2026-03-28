#!/usr/bin/env bash
set -euo pipefail

SOURCE_PATH="${1:?source path required}"
OUTPUT_PATH="${2:?output path required}"
SEARCH_TEXT="${3:-G90}"
REPLACEMENT_TEXT="${4:-G90}"
FILE_GLOB="${5:-*.nc}"

mkdir -p "$OUTPUT_PATH"

matches_glob() {
  local name="$1"
  case "$name" in
    $FILE_GLOB) return 0 ;;
    *) return 1 ;;
  esac
}

replace_file() {
  local src="$1"
  local dst="$2"
  mkdir -p "$(dirname "$dst")"
  python3 - "$src" "$dst" "$SEARCH_TEXT" "$REPLACEMENT_TEXT" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
search = sys.argv[3]
replace = sys.argv[4]
text = src.read_text(encoding="utf-8", errors="replace")
dst.write_text(text.replace(search, replace), encoding="utf-8", newline="\n")
PY
}

if [[ -d "$SOURCE_PATH" ]]; then
  while IFS= read -r -d '' file; do
    rel="${file#"$SOURCE_PATH"/}"
    dst="$OUTPUT_PATH/$rel"
    if matches_glob "$(basename "$file")"; then
      replace_file "$file" "$dst"
    else
      mkdir -p "$(dirname "$dst")"
      cp "$file" "$dst"
    fi
  done < <(find "$SOURCE_PATH" -type f -print0)
else
  filename="$(basename "$SOURCE_PATH")"
  dst="$OUTPUT_PATH/$filename"
  if matches_glob "$filename"; then
    replace_file "$SOURCE_PATH" "$dst"
  else
    cp "$SOURCE_PATH" "$dst"
  fi
fi

echo "OUTPUT_PATH=$OUTPUT_PATH"
