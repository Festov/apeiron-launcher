# Apeiron Launcher

**Portable Minecraft launcher for Windows** — instances, mod loaders, Microsoft sign-in, skins, and a multilingual UI.

Repository: [Festov/apeiron-launcher](https://github.com/Festov/apeiron-launcher) · Current version: **v1.6.2**

---

## English

### What is Apeiron?

Apeiron is a **single-folder, portable** Minecraft launcher (.NET 8 / WPF). Put `Apeiron.exe` anywhere — game files, instances, settings, and logs live next to it. No system-wide install and no launcher data in `%AppData%`.

It covers **download → mod loader → Java → launch**: vanilla and modded instances (Fabric, Quilt, Forge, NeoForge), Microsoft account login or offline play, skins with a 3D preview, and UI in **English, Russian, and Chinese** (or auto from the system language).

### Features

#### Instances & modding
- Multiple **instances** under `instances/` (saves, mods, config)
- **Vanilla** and modded: Fabric, Quilt, Forge, NeoForge
- **Modpack catalog** (Modrinth / CurseForge) when creating an instance
- **Mod browser** to install individual mods into an instance
- Export / import, full backup, verify game files, optional instance icon
- Per-instance RAM, JVM profiles, resource/shader pack folders
- Last-played tracking; CLI `--launch` (name/id) or `--launch` alone for last played

#### Play & install
- One-click **Play** with install-if-needed
- Download progress with cancel; resume via HTTP Range; manifest cache
- Auto **Oracle JDK** matched to the Minecraft version
- Install / game logs from the instance editor

#### Accounts & skins
- **Microsoft sign-in** (embedded WebView2), multi-account, DPAPI-encrypted tokens
- Offline-only mode and nickname validation
- Skin picker: **3D preview** (background color presets), defaults, history, PNG file, import by name/URL
- WebView2 Runtime check on startup

#### Launcher UX
- Obsidian Ember theme; onboarding; minimize to tray; optional close-on-launch
- **Single-instance** — a second start focuses the existing window
- Auto-update from GitHub Releases (zip / exe / optional delta patch)
- Languages: Auto, English, Russian, Chinese

### Requirements

- Windows 10/11 x64
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (Microsoft sign-in & skin preview)
- Internet for first-time Minecraft / Java download
- **.NET 8 SDK** only if you build from source

### Install (end users)

1. Download `Apeiron-<version>-win-x64.zip` from [Releases](https://github.com/Festov/apeiron-launcher/releases).
2. Extract to any folder and run `Apeiron.exe` (self-contained — no .NET install needed).
3. Create an instance and press Play. Oracle JDK may install automatically if Java is missing.

### Folder layout

```
Apeiron.exe
.minecraft/     # versions, libraries, assets (or custom path from Settings)
instances/      # per-instance mods, config, saves
config/         # settings, builds list, encrypted auth
logs/           # launcher and install logs
```

### Build & release (developers)

```bash
git clone https://github.com/Festov/apeiron-launcher.git
cd apeiron-launcher
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
dotnet build Apeiron.csproj -c Release
```

Release package (tests + single-file publish → `dist/`):

```powershell
.\build-release.ps1
# or: .\build-release.ps1 -SkipTests
```

Outputs: `dist/Apeiron.exe`, `dist/Apeiron-<version>-win-x64.zip`, `.sha256` checksums, and optionally a delta `.patch` between versions.

Tag a release to publish via GitHub Actions: `git tag v1.6.2 && git push origin v1.6.2`

---

## Русский

### Что такое Apeiron?

Apeiron — **portable-лаунчер Minecraft для Windows** (.NET 8 / WPF). Положите `Apeiron.exe` в любую папку — игра, сборки, настройки и логи рядом с exe.

Цикл **скачивание → модлоадер → Java → запуск**: Fabric / Quilt / Forge / NeoForge, вход Microsoft или офлайн, скины с 3D-превью. Языки: **русский, английский, китайский** (или авто).

### Возможности

- Сборки, каталог модпаков (Modrinth / CurseForge), браузер модов, бэкап, проверка файлов
- Play в один клик, авто-JDK, докачка, логи
- Microsoft (WebView2) / офлайн, скины с выбором фона превью
- Трей, онбординг, один экземпляр процесса, автообновление с GitHub
- CLI: `--launch`, `--help`

### Установка

1. Скачайте zip с [Releases](https://github.com/Festov/apeiron-launcher/releases).
2. Распакуйте и запустите `Apeiron.exe`.
3. Создайте сборку и нажмите «Играть».

Нужны Windows 10/11 x64 и [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/).

### Сборка

```powershell
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
.\build-release.ps1
```
