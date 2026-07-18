# ProCut Suite Desktop

Cross-platform CNC file monitoring, optional processing, ProCut Suite API processing, and destination delivery built with Avalonia UI and .NET.

This app was formerly CNC Sync. It is now the supported ProCut Suite Desktop app and replaces the older Go-based ProCutSuite-Desktop 0.1.x agent.

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
- ProCut Suite API processing setup mode for server-side G-code processing
- optional VPN preflight for destinations that require it
- password and private-key SSH authentication
- start at login desktop behavior instead of a service-style deployment
- clean separation between UI, core logic, and infrastructure

## Support and Telemetry

- The app shows an in-product notice that anonymised usage telemetry is collected for product improvements and support planning.
- The Help / About area can send sanitised recent logs and settings snippets to PostHog when a user explicitly chooses `Send Error Logs for Support`.
- Project-specific scripts can be offered as paid help.
- App bugs or improvements that could benefit everyone should be logged as GitHub issues for consideration.

## Release

- GitHub Actions release workflow: [`.github/workflows/release.yml`](.github/workflows/release.yml)
- Release process guide: [`docs/releasing.md`](docs/releasing.md)
- Packaging scripts:
  - macOS: [`packaging/macos/package-app.sh`](packaging/macos/package-app.sh)
  - Windows installer/update packaging: [`packaging/windows/package-velopack.ps1`](packaging/windows/package-velopack.ps1)
  - Windows zip packaging: [`packaging/windows/package-zip.ps1`](packaging/windows/package-zip.ps1)
  - Linux: [`packaging/linux/package-tarball.sh`](packaging/linux/package-tarball.sh)
- Windows installer path:
  - Velopack `Setup.exe` installs by default to `%LocalAppData%\\3DTek.ProCutSuiteDesktop`
- Windows update feed:
  - packaged releases use the public GitHub Pages update feed and Velopack metadata

### Quick Release Steps

1. Bump the app version in [`Directory.Build.props`](Directory.Build.props).
2. Keep packaging defaults in sync if needed:
   - [`packaging/windows/package-velopack.ps1`](packaging/windows/package-velopack.ps1)
   - [`packaging/windows/package-zip.ps1`](packaging/windows/package-zip.ps1)
3. Build locally and confirm it passes.
4. Commit to `main`.
5. Create and push a release tag in the `v1.0.x` line.
6. The tag triggers [`.github/workflows/release.yml`](.github/workflows/release.yml), which builds and publishes the release artifacts.

### Current Versioning Convention

- App/package version and Git tags/releases both use the `1.0.x` line.
- Pushing `main` alone does not create a release build. The release build is triggered by pushing a `v*` tag.

## Notes

- Local builds are still useful for testing.
- Tagged releases are intended to produce the official GitHub release artifacts.
- FTP compatibility for older CNC controllers may need a destination-level data mode option: `Auto Passive`, `Passive`, or `Active`.
