# CNC Sync User Guide

## What CNC Sync Does

CNC Sync watches one or more local folders, optionally runs a local processing step, and delivers the prepared result to a reusable destination.

The app is built around four reusable setup types:

- `App Settings`
  - global behaviour for the app itself
- `Destinations`
  - named FTP, SFTP, SCP, Local Folder, or Network Share targets that can be reused by many watch profiles
- `Processing Setups`
  - named processing rules, including simple passthrough or external scripts
- `Watch Folders`
  - the actual monitored folders that combine a watch path, a processing setup, and a destination

## Typical Setup Flow

1. Create a `Destination`.
2. Create a `Processing Setup`.
3. Create a `Watch Folder` that points to a local watch folder and staging folder, then select the destination and processing setup it should use.
4. Validate settings.
5. Start monitoring.

## App Settings

Use `App Settings` for behaviour that applies to the whole app:

- `Launch At Login`
  - start CNC Sync when the user logs in
- `Start Minimized`
  - start hidden/minimized when supported on the current platform
- `Import Settings...`
  - browse for a `settings.json` file from another machine or previous installation and import it into the current app profile
- `Validate Settings`
  - check the current configuration for missing or invalid items

Settings are saved automatically as you change them.
Imported settings replace the current profile configuration.
Passwords and SSH key passphrases are stored in the local OS secret store, so after moving a settings file between machines you may need to re-enter those secrets on the new machine.

## Destinations

A destination defines one reusable delivery target:

- `Destination Name`
  - friendly label shown in selectors
- `Remote Base Path`
  - optional base path on the target such as `/uploads`

Supported destination types:

- `FTP`
  - host, port, optional anonymous mode, and username/password when needed
- `SFTP`
  - host, port, username, and either password or private-key authentication
- `SCP`
  - host, port, username, and either password or private-key authentication
- `Local Folder`
  - a normal local filesystem path
- `Network Share`
  - an SMB share such as a NAS or another computer on the network

SSH destination authentication:

- `Password`
  - standard username/password sign-in
- `Private Key`
  - key file path plus optional key passphrase

Network Share notes:

- on macOS, explicit sign-in to a Mac SMB share may require enabling that account under `Windows File Sharing`
- on Linux, SMB shares work best when they are already mounted by the desktop environment
- on Linux, explicit SMB username/password access may still require mounting the share first in the desktop file manager
- if a destination requires a VPN first, choose it in `Required VPN`
- VPN profiles used by CNC Sync must be able to connect automatically without prompting for user interaction
- if enabled, `Check destination before starting VPN` tries the destination directly first and only starts the VPN if direct access fails
- if enabled, `Disconnect VPN When Finished` disconnects a VPN only when CNC Sync had to connect it itself, and only after a short idle window
- `FTP Data Mode` can be set to:
  - `Auto Passive`
    - the usual choice
  - `Passive`
    - useful if uploads start but stall
  - `Active`
    - only for servers that need to connect back to your machine

The effective remote path is built from:

- destination base path
- plus watch profile additional remote path

Example:

- destination base path: `/uploads`
- watch additional remote path: `/watch1`
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

You can import a shared script bundle from the left side of the `Processing Setups` tab. CNC Sync copies it to your local Scripts folder.

Fields:

- `Mode`
  - choose `ExternalScript`
- `Runner`
  - use Auto unless your script needs a specific way to run
    - `Auto`
    - `PowerShell`
    - `Bash`
    - `Python`
    - `Command`
    - `Direct`
- `Script Path`
  - the local file CNC Sync should run
- `Custom Script Source URL`
  - a shared customer script source used by `Check / Import` on the left side of the `Processing Setups` tab
- `Arguments Template`
  - extra arguments passed to the script at runtime

Supported placeholders:

- `{sourcePath}`
  - the detected source file or folder
- `{outputPath}`
  - the prepared output folder where processed files should be written
- `{scriptPath}`
  - the selected script path

Any other text is passed through literally, so you can add extra fixed arguments.

Example:

```text
"{sourcePath}" "{outputPath}"
```

Example with one extra literal text argument:

```text
"{sourcePath}" "{outputPath}" "watch1"
```

The bundled `legacy_revision` script uses one extra flag to enable the CYC Y-coordinate update:

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

### Shared Custom Script Imports

If you are receiving customer-specific scripts from a shared source:

- paste the shared link once into `Custom Script Source URL` in the left Processing Setups panel
- click `Check / Import`
- CNC Sync imports the files into the local Scripts folder under `Imported/CustomSource`
- each processing setup can then choose whichever imported local script it needs using `Script Path`

Import behavior:

- imported scripts stay local and are executed locally
- repeated checks only archive/replace files that actually changed
- unchanged files are left alone
- old local versions are archived with a timestamp before replacement
- junk files such as `.DS_Store`, `._*`, `Thumbs.db`, `desktop.ini`, and `ehthumbs.db` are ignored during import

## Watch Folders

A folder watch ties everything together.

Fields:

- `Profile Name`
  - friendly label for this watch profile
- `Enabled`
  - turns this watch folder on for live monitoring and manual catch-up
- `Watch Folder`
  - local folder being watched
- `Staging Folder`
  - local folder used for prepared output
- `Additional Remote Path`
  - optional path appended under the selected destination base path
- `Work Item Mode`
  - choose whether CNC Sync should process each change on its own, or group changes by the main project folder
- `Processing Setup`
  - which processing rule to run
- `Destination`
  - which destination to upload to
- `Required Quiet Time`
  - how long files/folders must stay unchanged before processing starts
- `Stability Check Interval`
  - how often CNC Sync checks pending items to see if they are ready

`Individual files and folders` is the general-purpose mode and reacts to each change on its own.

`Grouped project folders` is for job-folder workflows such as Mozaik exports. It is the safer choice when a destination replaces remote folder contents before upload.

## Monitoring

The `Monitoring` panel shows:

- whether monitoring is running
- which watch profiles are active
- the current task
- the last processing summary

`Start` begins folder monitoring.
`Stop` stops folder monitoring.

If enabled watch profiles exist and settings validate successfully, the app can auto-start monitoring on launch.
Changes to monitoring-related settings are applied live by reloading monitoring automatically.

## Manual Catch-Up

`Manual Catch-Up` is for reconciling missed uploads.

It uses the local staging folder as a retry outbox.
It:

- reads the selected watch profile
- looks for items still waiting in staging because a previous delivery did not complete successfully
- retries those staged items to the selected destination
- removes staged items after a successful upload

It does not reconstruct files that appeared while CNC Sync was not running.

Use this when:

- the destination was unavailable
- monitoring was stopped
- staged items are still waiting locally after a failed delivery

Scheduled catch-up uses the same staged-outbox model.
It can only retry items that made it into staging while CNC Sync was running.

## Activity Log

The `Activity` tab shows recent events with:

- time
- source/profile
- message

Timestamps include milliseconds so quick processing sequences are easier to follow.
The log text is selectable for copy/paste.
The same activity stream is also written to a text file in the app data folder. On Linux that is typically `~/.config/CNC Sync/activity.log`, on macOS it is `~/Library/Application Support/CNC Sync/activity.log`, and on Windows it is typically `%AppData%\CNC Sync\activity.log`.

## Help / About

The `Help / About` tab includes:

- setup reminders
- destination and VPN notes
- shared custom-script import reminders
- a link to the project page
- a link to the update log / GitHub releases page

## Notes

- FTP timestamps are not relied on for catch-up decisions.
- Finder can be misleading for FTP or SMB browsing and refresh; use a dedicated client or your normal file manager when verifying remote contents.
- Metadata junk such as `.DS_Store`, `._*`, `Thumbs.db`, `desktop.ini`, and `ehthumbs.db` is ignored during watching, staging, catch-up, and upload.
- On Linux AppImage builds, make the file executable before first launch. Launch At Login uses the current AppImage path, so move it to its long-term location before enabling login startup.
