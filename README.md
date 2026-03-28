# CBWSS Sync

Cross-platform rebuild of CBWSS Sync using Avalonia UI and .NET.

## Workspace Layout

- `docs/`
  - active rebuild notes and specs
- `src/CBWSSSync.App`
  - Avalonia desktop application
- `src/CBWSSSync.Core`
  - platform-agnostic domain and application logic
- `src/CBWSSSync.Infrastructure`
  - file system, FTP, config persistence, startup integration
- `tests/CBWSSSync.Tests`
  - unit tests
- `Original/`
  - archived WinForms/service-based app kept for reference

## Product Direction

This rebuild is intentionally simpler than the original app:

- tray/menu bar desktop app
- start at login instead of running as a Windows Service
- Windows and macOS first
- clean separation between UI, core logic, and infrastructure

See [`docs/avalonia-rebuild-spec.md`](/Users/benharper/Coding/CBWSS-Sync/docs/avalonia-rebuild-spec.md) for the current rebuild spec.

## Next Steps

1. Define the first real domain models and configuration schema.
2. Build the monitoring and processing pipeline in `Core`.
3. Add FTP and startup integration in `Infrastructure`.
4. Replace the placeholder Avalonia shell with dashboard, settings, and activity views.
