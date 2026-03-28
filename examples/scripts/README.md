# Example Processing Scripts

These scripts are starter examples for CNC Sync `Processing Setups`.

All examples assume the default argument contract:

```text
"{sourcePath}" "{outputPath}"
```

Where:

- `{sourcePath}` is the detected local file or folder
- `{outputPath}` is the prepared output folder CNC Sync expects you to fill

Each script should:

1. write processed output into the provided output folder, or print a replacement output path
2. exit `0` on success
3. exit non-zero on failure

Optional stdout contract:

```text
OUTPUT_PATH=/path/to/final/output
```

If that line is printed, CNC Sync uploads that folder instead of the default output folder.

## Included Examples

- `windows/encoding_normalizer.ps1`
- `macos/encoding_normalizer.sh`
  - copies files through and normalizes common text-based CNC files to UTF-8

- `windows/gcode_text_replace.ps1`
- `macos/gcode_text_replace.sh`
  - copies files through and performs simple text replacement
  - optional extra args:
    - search text
    - replacement text
    - file glob

  Example arguments template:

  ```text
  "{sourcePath}" "{outputPath}" "M30" "M30\n%" "*.nc"
  ```

- `windows/latest_revision_picker.ps1`
- `macos/latest_revision_picker.sh`
  - keeps only the highest `Rxx` revision set when revisioned files exist

- `shared/cbwss_mozaik_example.py`
- `windows/cbwss_mozaik_example.ps1`
- `macos/cbwss_mozaik_example.sh`
  - example based on the legacy CBWSS/Mozaik flow
  - copies a project, selects the latest revision, builds `NC/` and `AutoStickLabel/`, moves files, and can optionally remove negative Y values in `.cyc` files

## Suggested Runner Modes

- `.ps1` files: `PowerShell` or `Auto`
- `.sh` files: `Bash` or `Auto`
- Python-based launchers:
  - use the provided `.ps1` or `.sh` wrapper
  - or point directly at the `.py` file and choose `Python`

## Notes

- These are examples, not universal shop-floor standards.
- Review them before using them in production.
- If your workflow needs extra arguments, add them to the `Arguments Template`.
