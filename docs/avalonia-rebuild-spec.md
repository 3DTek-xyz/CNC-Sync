# CBWSS Sync Rebuild Spec

## Goal

Rebuild CBWSS Sync as a clean, cross-platform desktop application using Avalonia UI and .NET.

The new app should:

- Run on Windows and macOS from a single modern codebase
- Be able to support Linux later without major redesign
- Live primarily as a tray/menu bar app
- Start automatically at user login if enabled
- Monitor folders, process files, and upload via FTP
- Avoid Windows Service architecture entirely
- Avoid installer/service-management complexity in the first version

This rebuild should treat the current WinForms app as a reference for workflow and business rules, not as a codebase to port directly.

## Product Direction

### Core Product Shape

The app is a lightweight desktop utility, not an enterprise service.

It should:

- Launch at login if the user enables it
- Start minimized to tray/menu bar
- Keep monitoring in the background while the user is logged in
- Provide a small desktop UI for configuration, status, logs, and manual actions

It should not:

- Install or manage a Windows Service
- Require admin privileges for normal use
- Include service install/uninstall/start/stop controls
- Depend on Windows-only APIs for core behavior

## Target Platforms

### Phase 1

- Windows
- macOS

### Phase 2

- Linux support if needed, using the same core architecture

## Tech Stack

- .NET 9+
- Avalonia UI
- MVVM architecture
- Shared .NET class libraries for core logic
- Structured logging via `Microsoft.Extensions.Logging`
- JSON configuration stored per user

Recommended project layout:

- `src/CBWSSSync.App`
- `src/CBWSSSync.Core`
- `src/CBWSSSync.Infrastructure`
- `src/CBWSSSync.Tests`

## Primary Use Cases

1. User launches the app and configures watch folder, FTP settings, and processing behavior.
2. App sits in tray/menu bar and watches for incoming project folders or files.
3. App waits until files are stable before processing.
4. App runs the required processing workflow.
5. App uploads the processed output to FTP if enabled.
6. User can inspect logs, current status, and recent activity.
7. User can manually trigger processing for a selected folder.
8. User can pause or resume monitoring without closing the app.

## Functional Requirements

### 1. Tray / Menu Bar Behavior

- App must support tray behavior on Windows and menu bar behavior on macOS
- App can be closed to tray instead of fully exiting
- Tray/menu bar menu should include:
  - Open
  - Start monitoring
  - Stop monitoring
  - Process folder manually
  - View recent status
  - Quit
- App should show lightweight notifications for important events:
  - Monitoring started
  - Processing completed
  - Upload succeeded
  - Upload failed
  - Configuration issue

### 2. Startup Behavior

- User can enable or disable launch at login
- App should restore its last monitoring state if configured to do so
- App should not require administrator privileges for startup

### 3. Configuration

Configuration should be editable in-app and stored in a user-writable JSON file.

Configuration areas:

- Watch folder
- Output / staging folder
- FTP server
- FTP port
- FTP username
- FTP password
- Anonymous FTP option
- Auto-upload on/off
- File stability delay
- File stability polling interval
- Processing mode
- External processor path, if supported
- Launch at login on/off
- Start minimized on/off
- Detailed logging on/off

Requirements:

- Config validation must be explicit
- Invalid config must never silently overwrite existing config
- Save and load behavior must be safe and recoverable
- If config is broken, app should present a clear error and preserve the bad file for recovery

### 4. Folder Monitoring

- Monitor a configured watch folder
- Detect new relevant work items
- Wait until files/folders are stable before processing
- Avoid duplicate processing
- Be resilient to bursty file creation
- Recover gracefully from watcher errors

The monitoring engine should be platform-agnostic and not depend on UI code.

### 5. Processing

The rebuild should preserve the real business behavior from the current app, but simplify the implementation.

Processing capabilities may include:

- Identifying the correct project or revision to process
- Organizing files into expected output structure
- Applying file transformations
- Supporting the existing internal processing modes
- Optionally supporting an external processor command

Requirements:

- Processing rules should be encapsulated in dedicated services
- Processing should produce a clear result object
- Failures should be reported clearly without crashing the app
- Manual and automatic processing should use the same pipeline

### 6. FTP Upload

- Test FTP connection from the UI
- Upload processed output folders/files
- Surface clear error messages
- Show upload status and recent results
- Support browsing or selecting remote target path only if genuinely needed

Keep FTP scope lean in v1:

- Focus on connection test and upload
- Do not rebuild elaborate remote browsing unless it proves necessary

### 7. Logs and Status

The app should have a simple status and activity view showing:

- Monitoring state
- Last processed item
- Last upload result
- Current action
- Recent log entries

Logging requirements:

- Human-readable rolling log files
- In-app recent log viewer
- Clear separation between info, warning, and error levels

### 8. Manual Actions

User should be able to:

- Start monitoring
- Stop monitoring
- Reload configuration
- Test FTP
- Manually process a selected folder
- Open log folder

## Non-Functional Requirements

### Cross-Platform

- Core logic must be OS-agnostic wherever possible
- Platform-specific code should be isolated behind interfaces
- Boot/login integration should be implemented via small platform adapters

### Reliability

- App should keep running for long sessions without leaking resources
- Background tasks should be cancellable
- No fire-and-forget async for critical operations
- File watcher pipeline should avoid blocking sleeps on critical threads

### Simplicity

- Prefer fewer features done well over broad feature parity
- Remove anything tied to service management and MSI-driven workflow
- Keep the first release small and supportable

### Maintainability

- Clear separation of UI, application logic, and infrastructure
- Small services with focused responsibilities
- Unit tests for configuration, watcher logic, processing, and FTP orchestration

## Out of Scope For V1

- Windows Service support
- Service install/uninstall/start/stop UI
- MSI-driven service lifecycle management
- Complex remote FTP file browser
- Auto-update framework
- WebView-based UI features
- Advanced installer customization
- Admin-only workflows unless absolutely required
- VPN detection / launch integration until the core monitoring, processing, and FTP workflow is stable

## Suggested Architecture

### App Layer

Responsibilities:

- Avalonia views and view models
- Tray/menu bar integration
- User commands
- Notifications
- Settings screens

### Core Layer

Responsibilities:

- Domain models
- Processing pipeline
- Monitoring orchestration
- Validation rules
- Result objects

### Infrastructure Layer

Responsibilities:

- File system watching
- FTP implementation
- Config file persistence
- Logging setup
- Startup-at-login integration

## Suggested Main Screens

### 1. Dashboard

- Monitoring status
- Current folder being processed
- Last successful upload
- Last error
- Start/stop controls

### 2. Settings

- Watch folder
- Output folder
- FTP settings
- Processing options
- Startup options
- Logging options

### 3. Activity / Logs

- Recent activity list
- Errors and warnings
- Open log folder action

### 4. Manual Process

- Pick folder
- Run processing
- Show result summary

## Migration Guidance

Reuse from the current project:

- Business rules
- Sample configurations
- Processing expectations
- FTP workflow expectations
- Real-world folder and file examples

Do not directly carry forward:

- WinForms UI structure
- Windows Service model
- Service-control UI
- Current large-form architecture
- Build output and installer artifacts

## Open Questions

- What exact processing modes are still needed in v2?
- Is remote FTP browsing actually required, or only FTP upload/test?
- Should the app support both folder-level and file-level watch modes in v1?
- Is Linux support a requirement soon, or only a future option?
- Should auto-update be added later, and if so, by what mechanism?

## Deferred Todo

- After the core app is working reliably, evaluate optional VPN integration for environments where FTP servers are only reachable over a private network.
- Preferred scope: detect whether the FTP target is reachable, report when a VPN is required, and optionally trigger an already-installed OS or vendor VPN connection.
- Avoid implementing VPN protocols or embedding a custom VPN stack inside the app.

## Definition of Done For V1

V1 is complete when:

- App runs on Windows and macOS
- User can configure it without editing files manually
- App can start at login if enabled
- App sits in tray/menu bar and monitors in background
- App detects stable work items reliably
- App processes files using the required workflow
- App uploads via FTP successfully
- App exposes clear logs and status
- No service-management code or UI remains in the new product
