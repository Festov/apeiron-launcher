# Apeiron Launcher — Architecture

Portable Minecraft launcher for Windows (.NET 8 / WPF).

## Layers

```mermaid
flowchart TB
    subgraph UI["UI (WPF)"]
        MW[MainWindow]
        VM[MainViewModel]
        Dialogs[Settings / AddBuild / EditBuild / Auth]
    end

    subgraph Orchestration["Orchestration"]
        LO[LauncherOrchestrator]
        PO[PlayOrchestrator]
        LC[LaunchCoordinator]
        IUC[InstallUiCoordinator]
    end

    subgraph Core["Core Services"]
        BM[BuildManager]
        BIS[BuildInstallService]
        MS[MinecraftService]
        LS[LoaderService]
        AS[AuthService]
        VL[VersionLauncher]
    end

    MW --> VM
    VM -->|bindings| MW
    MW --> LO
    LO --> PO
    LO --> LC
    MW --> IUC
    PO --> BIS
    PO --> MS
    PO --> VL
    BIS --> MS
    BIS --> LS
```

## Install flow

1. User clicks **Play** → `LauncherOrchestrator.ValidateBuild`
2. Java ensured via `JavaService`
3. If not installed → `PlayOrchestrator.InstallIfNeededAsync`
   - `MinecraftService.DownloadVanillaMinecraft` (manifest cache + HTTP retry)
   - `LoaderService.InstallLoader` (Fabric / Quilt / Forge / NeoForge)
   - Optional `FabricApiService`
4. `LaunchCoordinator.ResolveLaunchIdentityAsync` (Microsoft or offline)
5. `PlayOrchestrator.LaunchGameAsync` → `VersionLauncher` spawns JVM process

## Data on disk

```
Apeiron.exe
├── .minecraft/       # Shared game assets (versions, libraries)
├── instances/<id>/   # Per-instance saves, mods, config
├── config/
│   ├── settings.json
│   ├── builds.json
│   └── auth.json     # DPAPI-encrypted
└── logs/
```

## Key extension points

| Area | Entry point |
|------|-------------|
| UI state | `MainViewModel` — status, play button, download progress bindings |
| New mod loader | `LoaderService.InstallLoader` switch |
| CLI | `LaunchArgsParser` → `MainWindow` quick launch |
| Modpack import | `ModpackImportService` / drag-drop on `MainWindow` |
| Auto-update | `LauncherUpdateService` + GitHub Releases |

## Tests

Unit tests live in `Apeiron.Tests/` (178+). Run:

```bash
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
```

Manual checklist: [TESTING.md](TESTING.md)
