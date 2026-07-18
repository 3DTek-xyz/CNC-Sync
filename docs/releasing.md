# Releasing ProCut Suite Desktop

ProCut Suite Desktop was formerly CNC Sync. The repo, solution, and some internal project names still use `CNCSync`, but release artifacts use the ProCut Suite Desktop product name.

This is the current bump, tag, and build process used for official releases.

## Version Numbers

- The app/package version is stored in [`Directory.Build.props`](../Directory.Build.props).
- The repo currently uses Git tags on the `v1.0.x` line.
- The app/package version uses the same `1.0.x` line:
  - app version: `1.0.x`
  - release tag: `v1.0.x`

## Files To Check Before Releasing

- [`Directory.Build.props`](../Directory.Build.props)
- [`packaging/windows/package-velopack.ps1`](../packaging/windows/package-velopack.ps1)
- [`packaging/windows/package-zip.ps1`](../packaging/windows/package-zip.ps1)
- [`.github/workflows/release.yml`](../.github/workflows/release.yml)

## Local Release Steps

1. Update the app version in [`Directory.Build.props`](../Directory.Build.props).
2. Keep the Windows packaging script defaults aligned if they still carry a hard-coded version:
   - [`packaging/windows/package-velopack.ps1`](../packaging/windows/package-velopack.ps1)
   - [`packaging/windows/package-zip.ps1`](../packaging/windows/package-zip.ps1)
3. Build locally:

```bash
dotnet build /Users/benharper/Coding/CBWSS-Sync/CNCSync.sln -m:1 -p:BuildInParallel=false
```

4. Commit the release changes to `main`.
5. Create a tag on the current Git release line, for example:

```bash
git tag v1.0.72
git push origin main
git push origin v1.0.72
```

## What GitHub Actions Does

- Pushing `main` updates the repo and Pages content.
- Pushing a `v*` tag triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml).
- The `Release` workflow:
  - builds Windows, macOS, and Linux packages
  - creates a GitHub release that only shows the human-facing installers
  - publishes the Velopack update feed separately for the Pages site

### Release Asset Split

- GitHub Releases should stay focused on the files people install manually.
- The Pages update feed keeps the Velopack metadata and package files needed for auto-update.
- If you need to change the release shape, update both workflows together so the release page and update feed stay in sync.

## Important Notes

- Pushing `main` by itself is not the full release process.
- The actual release build is tag-driven.
- If versioning policy changes later, update this file at the same time so the process stays explicit.
