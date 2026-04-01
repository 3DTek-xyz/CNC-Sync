# CNC Sync

Cross-platform CNC file monitoring, optional processing, and destination delivery built with Avalonia UI and .NET.

- Documentation: [3dtek-xyz.github.io/CNC-Sync](https://3dtek-xyz.github.io/CNC-Sync/)
- Releases: [github.com/3DTek-xyz/CNC-Sync/releases](https://github.com/3DTek-xyz/CNC-Sync/releases)

## Workspace Layout

- `docs/`
  - public documentation and download site content
- `src/CNCSync.App`
  - Avalonia desktop application
- `src/CNCSync.Core`
  - platform-agnostic domain and application logic
- `src/CNCSync.Infrastructure`
  - file system, protocol services, config persistence, startup integration
- `tests/CNCSync.Tests`
  - unit tests

## Product Direction

The current app is built around reusable destinations, processing setups, and watch folders:

- FTP, SFTP, SCP, Local Folder, and Network Share destinations
- optional VPN preflight for destinations that require it
- password and private-key SSH authentication
- start at login desktop behavior instead of a service-style deployment
- clean separation between UI, core logic, and infrastructure

## Release

- GitHub Actions release workflow: [`.github/workflows/release.yml`](.github/workflows/release.yml)
- Packaging scripts:
  - macOS: [`packaging/macos/package-app.sh`](packaging/macos/package-app.sh)
  - Windows installer/update packaging: [`packaging/windows/package-velopack.ps1`](packaging/windows/package-velopack.ps1)
  - Windows zip packaging: [`packaging/windows/package-zip.ps1`](packaging/windows/package-zip.ps1)
  - Linux: [`packaging/linux/package-tarball.sh`](packaging/linux/package-tarball.sh)
- Windows installer path:
  - Velopack `Setup.exe` installs by default to `%LocalAppData%\\3DTek.CNCSync`
- Windows update feed:
  - packaged Windows releases check GitHub Releases for updates

## Notes

- Local builds are still useful for testing.
- Tagged releases are intended to produce the official GitHub release artifacts.
- FTP compatibility for older CNC controllers may need a destination-level data mode option: `Auto Passive`, `Passive`, or `Active`.
