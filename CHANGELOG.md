# Changelog

## 1.4.0 — 2026-07-09

### Features (Stage 5)
- Launcher auto-update via GitHub Releases (repo embedded at build from `git remote`)
- Full instance backup (mods, config, saves) from edit dialog
- Modpack import as new instance (zip with `overrides/` supported)
- Offline-only mode — hides Microsoft sign-in
- Per-instance RAM override in edit dialog

## 1.3.0 — 2026-07-09

### UX (Stage 2)
- MC version search in «New instance» window
- Install progress stages: Minecraft → loader → ready
- Offline nickname validation with inline error before Play
- Shorter install failure message; «Open log» in status bar (no dialog)
- Cancel button pinned to the right during downloads
- Tab order and tooltips in create/settings dialogs

### Java
- Oracle JDK mapping per MC version (Java 8 for 1.16 and below)
- Java 8 download via Oracle OTN + Adobe ColdFusion mirror (official Oracle installer)
- System install to `Program Files\Java\`

### Engineering (Stage 3)
- GitHub Actions CI: build + test on Windows
- Expanded unit tests: BuildInfo, LoaderService, GameLaunchService, InstallSession, export/import
- Refactor: `InstallSession` (install logs/cancel), `GameLaunchService` (launch identity)

### Release (Stage 4)
- `build-release.ps1`: tests → publish → zip in `dist/`
- `RELEASE.txt` bundled with published build
- GitHub Release workflow on `v*` tags

### Stability (Stage 1)
- Smoke-test checklist (`TESTING.md`)
- Install error logs with «Open log» button
- Modded install fixes (Fabric/Quilt/Forge, Java selection, library downloads)
- Encrypted auth tokens, atomic settings saves

## 1.2.0

- Theme service, offline nick, export/import builds, download cancellation
- Portable Java (replaced by system Oracle JDK in 1.3.0)
