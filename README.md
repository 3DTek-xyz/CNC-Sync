# CNC Sync

Cross-platform CNC file monitoring, processing, and FTP upload utility built with Avalonia UI and .NET.

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

## Release

- Current target version: `0.1.0`
- GitHub Actions release workflow: [`.github/workflows/release.yml`](/Users/benharper/Coding/CBWSS-Sync/.github/workflows/release.yml)
- Packaging scripts:
  - macOS: [`packaging/macos/package-app.sh`](/Users/benharper/Coding/CBWSS-Sync/packaging/macos/package-app.sh)
  - Windows: [`packaging/windows/package-zip.ps1`](/Users/benharper/Coding/CBWSS-Sync/packaging/windows/package-zip.ps1)
  - Linux: [`packaging/linux/package-tarball.sh`](/Users/benharper/Coding/CBWSS-Sync/packaging/linux/package-tarball.sh)

## Notes

- Local builds are still useful for testing.
- Tagged releases like `v0.1.0` are intended to produce the official GitHub release artifacts.
