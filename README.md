# Apeiron Launcher

Minecraft launcher for Windows with Microsoft authentication, Fabric/Quilt/Forge/NeoForge support, and bilingual UI (RU/EN).

## Requirements

- Windows 10/11 x64
- .NET 8 SDK (for building)
- WebView2 Runtime (for Microsoft sign-in)

## Build

```bash
dotnet build Apeiron.csproj -c Release
```

Output: `bin/Release/net8.0-windows/win-x64/Apeiron.exe`

### Release package (single-file, no SDK required)

```powershell
.\build-release.ps1
```

This runs tests, publishes a self-contained `Apeiron.exe`, and creates:

| Path | Description |
|------|-------------|
| `bin/Release/net8.0-windows/win-x64/publish/Apeiron.exe` | Portable launcher (~75 MB) |
| `dist/Apeiron-<version>-win-x64.zip` | Zip for distribution (exe + `RELEASE.txt`) |
| `dist/Apeiron-<version>-win-x64.zip.sha256` | SHA256 checksum for auto-update verification |

Skip tests when iterating locally:

```powershell
.\build-release.ps1 -SkipTests
```

### GitHub Release

Push a version tag to publish the zip automatically:

```bash
git tag v1.4.0
git push origin v1.4.0
```

Repository: [Festov/apeiron-launcher](https://github.com/Festov/apeiron-launcher)

Workflow: `.github/workflows/release.yml`

## Folder layout

```
Apeiron.exe
.minecraft/          # Game files (versions, libraries, assets)
instances/           # Per-build saves, mods, config
config/              # settings.json, builds.json, auth.json (encrypted)
logs/                # Launcher and game logs
```

Java is installed system-wide to `C:\Program Files\Java\` (Oracle JDK), selected automatically by Minecraft version.

## Minecraft ↔ Java

| Minecraft | Java |
|-----------|------|
| 26.x | 25 |
| 1.20.5 – 1.21.x | 21 |
| 1.18 – 1.20.4 | 17–21 |
| 1.17.x | 17 |
| 1.16.x and below | 8 |

## Features

- Vanilla and modded instances (Fabric, Quilt, Forge, NeoForge)
- Microsoft account sign-in with encrypted token storage (DPAPI)
- Offline-only mode (hide Microsoft sign-in in settings)
- Offline play with validated nickname (3–16 chars, `a-z`, `0-9`, `_`)
- Auto Oracle JDK install per Minecraft version (silent, background)
- Instance export/import and full backup (mods, config, saves)
- Modpack import as new instance (zip, including `overrides/`) — button or drag-and-drop
- CLI: `--launch <instance>` and `--help`
- Per-instance RAM override (or global default in settings)
- Launcher auto-update from GitHub Releases (repository embedded at build time)
- Mod metadata from `fabric.mod.json` / `mods.toml`
- Download cancellation, install logs, SHA1 verification

## Tests

```bash
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
```

Checklists: [TESTING.md](TESTING.md)

Architecture: [ARCHITECTURE.md](ARCHITECTURE.md)

## Settings

- **RAM** — global default (capped to ~75% of system memory)
- **Language** — auto / English / Russian
- **Offline only** — hide Microsoft sign-in
- **Check for updates** — prompt on startup; manual check in settings

Minecraft data is stored in `.minecraft` next to the executable.
