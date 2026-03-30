# CNC Sync User Guide

## What CNC Sync Does

CNC Sync watches one or more local folders, optionally runs a local processing step, and uploads the prepared result to an FTP destination.

The app is built around four reusable setup types:

- `App Settings`
  - global behaviour for the app itself
- `FTP Servers`
  - named FTP destinations that can be reused by many watch profiles
- `Processing Setups`
  - named processing rules, including simple passthrough or external scripts
- `Watch Folders`
  - the actual monitored folders that combine a watch path, a processing setup, and an FTP setup

## Typical Setup Flow

1. Create an `FTP Server`.
2. Create a `Processing Setup`.
3. Create a `Watch Folder` that points to a local watch folder and staging folder, then select the FTP server and processing setup it should use.
4. Validate settings.
5. Start monitoring.

## App Settings

Use `App Settings` for behaviour that applies to the whole app:

- `Launch At Login`
  - start CNC Sync when the user logs in
- `Start Minimized`
  - start hidden/minimized when supported on the current platform
- `Load Settings`
  - reload the saved configuration from disk
- `Validate Settings`
  - check the current configuration for missing or invalid items

Settings are saved automatically as you change them.

## FTP Servers

An FTP server defines one reusable destination:

- `Destination Name`
  - friendly label shown in selectors
- `FTP Host`
  - server hostname or IP address
- `Remote Base Path`
  - optional base path on the server such as `/uploads`
- `FTP Port`
  - usually `21` unless the server uses another port
- `Use Anonymous FTP`
  - if enabled, username/password are not used
- `FTP Username` / `FTP Password`
  - credentials for non-anonymous access
- `Auto Upload`
  - whether successful processing should upload automatically

The effective remote path is built from:

- FTP server base path
- plus watch profile remote subfolder

Example:

- FTP base path: `/uploads`
- watch remote subfolder: `/watch1`
- final remote target: `/uploads/watch1`

## Processing Setups

A processing setup defines what happens to a detected file or folder before upload.

### Default Upload

This is the default passthrough option.

- the source item is copied into the staging/output folder
- then the copied result is uploaded

Use this when no custom transformation is required.

### External Script

This runs a local script or executable before upload.

Fields:

- `Mode`
  - choose `ExternalScript`
- `Runner`
  - choose how to launch the script:
    - `Auto`
    - `PowerShell`
    - `Bash`
    - `Python`
    - `Command`
    - `Direct`
- `Script Path`
  - the local file to execute
- `Arguments Template`
  - arguments passed to the script at runtime

Supported placeholders:

- `{sourcePath}`
  - the detected source file or folder
- `{outputPath}`
  - the prepared output folder where processed files should be written
- `{scriptPath}`
  - the selected script path

Any other text is passed through literally, which means you can add extra fixed arguments if your script expects them.

Example:

```text
"{sourcePath}" "{outputPath}"
```

Example with one extra literal text argument:

```text
"{sourcePath}" "{outputPath}" "watch1"
```

The bundled CBWSS example uses one extra flag to enable the CYC Y-coordinate update:

```text
"{sourcePath}" "{outputPath}" --update-cyc-y
```

Script contract:

- exit code `0` means success
- non-zero means failure
- optional stdout line:

```text
OUTPUT_PATH=/path/to/final/output
```

If `OUTPUT_PATH=` is printed, CNC Sync uploads that folder.
If not, CNC Sync uploads the prepared output folder it already passed in.

## Watch Folders

A folder watch ties everything together.

Fields:

- `Profile Name`
  - friendly label for this watch profile
- `Enabled`
  - whether this profile participates in monitoring
- `Watch Folder`
  - local folder being watched
- `Staging Folder`
  - local folder used for prepared output
- `Remote Subfolder`
  - optional subfolder appended to the FTP setup base path
- `Processing Setup`
  - which processing rule to run
- `FTP Destination`
  - which FTP setup to upload to
- `Required Quiet Time`
  - how long files/folders must stay unchanged before processing starts
- `Stability Check Interval`
  - how often CNC Sync checks pending items to see if they are ready

## Monitoring

The `Monitoring` panel shows:

- whether monitoring is running
- which watch profiles are active
- the current task
- the last processing summary

`Start` begins folder monitoring.
`Stop` stops folder monitoring.

If enabled watch profiles exist and settings validate successfully, the app can auto-start monitoring on launch.

## Manual Catch-Up

`Manual Catch-Up` is for reconciling missed uploads.

It does not blindly upload everything.
It:

- reads the selected watch profile
- checks the target FTP directory
- compares local items against remote items
- processes/uploads only missing or changed items

Current comparison approach:

- files: `name + size`
- directories: name-based presence
- ignores metadata junk like `.DS_Store`, `Thumbs.db`, and `desktop.ini`

Use this when:

- the app was not running
- the FTP server was unavailable
- monitoring was stopped

## Activity Log

The `Activity` tab shows recent events with:

- time
- source/profile
- message

Timestamps include milliseconds so quick processing sequences are easier to follow.

## Notes

- FTP timestamps are not relied on for catch-up decisions.
- Finder can be misleading for FTP browsing and refresh; use a dedicated FTP client when verifying server contents.
- Old test folders left from earlier versions of the app may need to be cleaned up manually on the server.
