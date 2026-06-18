#!/usr/bin/env bash
set -euo pipefail

SOURCE_PATH="${1:-}"
OUTPUT_PATH="${2:-}"
UPDATE_CYC_Y="${3:-}"

if [[ -z "$SOURCE_PATH" || -z "$OUTPUT_PATH" ]]; then
  echo "Usage: legacy_revision.sh <sourcePath> <outputPath> [--update-cyc-y]" >&2
  exit 1
fi

if [[ ! -d "$SOURCE_PATH" ]]; then
  echo "legacy_revision expects a folder source path." >&2
  exit 1
fi

rm -rf "$OUTPUT_PATH"
mkdir -p "$OUTPUT_PATH/NC" "$OUTPUT_PATH/AutoStickLabel"

latest_revision="$(
  find "$SOURCE_PATH" -type f -iname '*.cyc' ! -iname 'ORIGINAL_*' -print0 |
    perl -0ne '
      while (/([^\/]+R(\d{2})(?:F)?\.cyc)\0/ig) {
        print "$2\n";
      }
    ' |
    sort -nr |
    head -n 1
)"

if [[ -z "$latest_revision" ]]; then
  echo "No CYC files with revision markers were found." >&2
  exit 1
fi

revision_tag="R$(printf "%02d" "$latest_revision")"
lower_revision_tag="$(printf '%s' "$revision_tag" | tr '[:upper:]' '[:lower:]')"

while IFS= read -r -d '' file; do
  name="$(basename "$file")"
  lower_name="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"

  case "$lower_name" in
    *.nc)
      if [[ "$lower_name" =~ ${lower_revision_tag}(f)?\.nc$ ]]; then
        cp "$file" "$OUTPUT_PATH/NC/$name"
      fi
      ;;
    *.cyc)
      if [[ "$lower_name" =~ ${lower_revision_tag}(f)?\.cyc$ && "$name" != ORIGINAL_* ]]; then
        destination="$OUTPUT_PATH/AutoStickLabel/$name"
        cp "$file" "$destination"
        if [[ "$UPDATE_CYC_Y" == "--update-cyc-y" ]]; then
          perl -0pi -e 's/(<Field Name="Y" Value=")-([\d\.]+(".*?>))/$1$2$3/g' "$destination"
        fi
      fi
      ;;
  esac
done < <(find "$SOURCE_PATH" -type f -print0)

while IFS= read -r -d '' file; do
  name="$(basename "$file")"
  lower_name="$(printf '%s' "$name" | tr '[:upper:]' '[:lower:]')"
  case "$lower_name" in
    *.xml|*.jpg|*.jpeg)
      cp "$file" "$OUTPUT_PATH/AutoStickLabel/$name"
      ;;
  esac
done < <(find "$SOURCE_PATH" -maxdepth 1 -type f -print0)

echo "OUTPUT_PATH=$OUTPUT_PATH"
