#!/usr/bin/env python3
import argparse
import pathlib
import re
import shutil
import sys


def newest_revision(files):
    pattern = re.compile(r"R(\d{2})", re.IGNORECASE)
    revisions = []
    for path in files:
        match = pattern.search(path.name)
        if match:
            revisions.append(int(match.group(1)))
    return max(revisions) if revisions else None


def normalize_y_values(text: str) -> str:
    pattern = re.compile(r'(<Field Name="Y" Value=")-([\d\.]+(".*?>))')
    return pattern.sub(r"\1\2\3", text)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source_path")
    parser.add_argument("output_path")
    parser.add_argument("--update-cyc-y", action="store_true")
    args = parser.parse_args()

    source_root = pathlib.Path(args.source_path)
    output_root = pathlib.Path(args.output_path)

    if not source_root.is_dir():
        print("CBWSS Mozaik example expects a folder source path.", file=sys.stderr)
        return 1

    if output_root.exists():
        shutil.rmtree(output_root)
    output_root.mkdir(parents=True, exist_ok=True)

    all_files = [path for path in source_root.rglob("*") if path.is_file()]
    cyc_files = [path for path in all_files if path.suffix.lower() == ".cyc" and not path.name.startswith("ORIGINAL_")]
    latest = newest_revision(cyc_files)

    if latest is None:
        print("No CYC files with revision markers were found.", file=sys.stderr)
        return 1

    revision_tag = f"R{latest:02d}"
    nc_dir = output_root / "NC"
    label_dir = output_root / "AutoStickLabel"
    nc_dir.mkdir(parents=True, exist_ok=True)
    label_dir.mkdir(parents=True, exist_ok=True)

    for path in all_files:
        lower = path.suffix.lower()
        if lower == ".nc" and revision_tag in path.name:
            shutil.copy2(path, nc_dir / path.name)
        elif lower == ".cyc" and revision_tag in path.name and not path.name.startswith("ORIGINAL_"):
            dst = label_dir / path.name
            shutil.copy2(path, dst)
            if args.update_cyc_y:
                original = dst.read_text(encoding="utf-8", errors="replace")
                updated = normalize_y_values(original)
                dst.write_text(updated, encoding="utf-8", newline="\n")
        elif lower in {".xml", ".jpg", ".jpeg"}:
            shutil.copy2(path, label_dir / path.name)

    print(f"OUTPUT_PATH={output_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
