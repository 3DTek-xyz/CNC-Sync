#!/usr/bin/env bash
set -euo pipefail

SOURCE_PATH="${1:?source path required}"
OUTPUT_PATH="${2:?output path required}"

mkdir -p "$OUTPUT_PATH"

normalize_text_file() {
  local src="$1"
  local dst="$2"
  mkdir -p "$(dirname "$dst")"
  python3 - "$src" "$dst" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
text = src.read_text(encoding="utf-8", errors="replace")
text = text.replace("\r\n", "\n").replace("\r", "\n")
dst.write_text(text, encoding="utf-8", newline="\n")
PY
}

is_text_extension() {
  case "${1,,}" in
    .nc|.tap|.gcode|.txt|.cyc|.xml|.csv|.ini) return 0 ;;
    *) return 1 ;;
  esac
}

if [[ -d "$SOURCE_PATH" ]]; then
  while IFS= read -r -d '' file; do
    rel="${file#"$SOURCE_PATH"/}"
    dst="$OUTPUT_PATH/$rel"
    ext=".${file##*.}"
    if is_text_extension "$ext"; then
      normalize_text_file "$file" "$dst"
    else
      mkdir -p "$(dirname "$dst")"
      cp "$file" "$dst"
    fi
  done < <(find "$SOURCE_PATH" -type f -print0)
else
  filename="$(basename "$SOURCE_PATH")"
  dst="$OUTPUT_PATH/$filename"
  ext=".${SOURCE_PATH##*.}"
  if is_text_extension "$ext"; then
    normalize_text_file "$SOURCE_PATH" "$dst"
  else
    cp "$SOURCE_PATH" "$dst"
  fi
fi

echo "OUTPUT_PATH=$OUTPUT_PATH"
