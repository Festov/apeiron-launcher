# Installation & setup

[English](#english) · [Русский](#русский)

---

## English

### Requirements

- Windows 10/11 x64
- **WebView2 Runtime** — required for Microsoft sign-in ([download](https://developer.microsoft.com/microsoft-edge/webview2/))
- **.NET 8 SDK** — only if you build from source

### End users (portable build)

1. Download the latest `Apeiron-<version>-win-x64.zip` from [GitHub Releases](https://github.com/Festov/apeiron-launcher/releases).
2. Extract to any folder (e.g. `D:\Games\Apeiron\`).
3. Run `Apeiron.exe`. No installer or .NET runtime on the machine is required — the build is self-contained.
4. On first launch the launcher may download **Oracle JDK** automatically for the selected Minecraft version (installed to `C:\Program Files\Java\`).

Game data is stored next to the executable (see [Folder layout](#folder-layout)).

### Build from source

```bash
git clone https://github.com/Festov/apeiron-launcher.git
cd apeiron-launcher
dotnet build Apeiron.csproj -c Release
```

Output: `bin/Release/net8.0-windows/win-x64/Apeiron.exe`

### Release package (single-file)

```powershell
.\build-release.ps1
```

Runs tests, publishes a self-contained `Apeiron.exe`, and creates:

| Path | Description |
|------|-------------|
| `bin/Release/net8.0-windows/win-x64/publish/Apeiron.exe` | Portable launcher (~75 MB) |
| `dist/Apeiron-<version>-win-x64.zip` | Zip for distribution (exe + `RELEASE.txt`) |
| `dist/Apeiron-<version>-win-x64.zip.sha256` | SHA256 checksum for auto-update verification |

Skip tests when iterating locally:

```powershell
.\build-release.ps1 -SkipTests
```

### GitHub Release (maintainers)

Push a version tag to publish the zip automatically:

```bash
git tag v1.5.0
git push origin v1.5.0
```

Repository: [Festov/apeiron-launcher](https://github.com/Festov/apeiron-launcher)  
Workflow: `.github/workflows/release.yml`

### Folder layout

```
Apeiron.exe
.minecraft/          # Game files (versions, libraries, assets)
instances/           # Per-instance saves, mods, config
config/              # settings.json, builds.json, auth.json (encrypted)
logs/                # Launcher and game logs
```

### Minecraft ↔ Java

Java is picked automatically per Minecraft version and installed if missing.

| Minecraft | Java |
|-----------|------|
| 26.x | 25 |
| 1.20.5 – 1.21.x | 21 |
| 1.18 – 1.20.4 | 17–21 |
| 1.17.x | 17 |
| 1.16.x and below | 8 |

### Settings

- **RAM** — global default (capped to ~75% of system memory); per-instance override in the instance editor
- **Language** — auto / English / Russian
- **Offline only** — hide Microsoft sign-in
- **Check for updates** — prompt on startup; manual check in settings

---

## Русский

### Требования

- Windows 10/11 x64
- **WebView2 Runtime** — для входа через Microsoft ([скачать](https://developer.microsoft.com/microsoft-edge/webview2/))
- **.NET 8 SDK** — только при сборке из исходников

### Для пользователей (portable-сборка)

1. Скачайте актуальный `Apeiron-<version>-win-x64.zip` в [GitHub Releases](https://github.com/Festov/apeiron-launcher/releases).
2. Распакуйте в любую папку (например, `D:\Games\Apeiron\`).
3. Запустите `Apeiron.exe`. Установщик и .NET на компьютере не нужны — сборка self-contained.
4. При первом запуске лаунчер может **автоматически установить Oracle JDK** под выбранную версию Minecraft (в `C:\Program Files\Java\`).

Данные игры хранятся рядом с exe (см. [Структура папок](#структура-папок)).

### Сборка из исходников

```bash
git clone https://github.com/Festov/apeiron-launcher.git
cd apeiron-launcher
dotnet build Apeiron.csproj -c Release
```

Результат: `bin/Release/net8.0-windows/win-x64/Apeiron.exe`

### Релизный пакет (один exe)

```powershell
.\build-release.ps1
```

Запускает тесты, публикует self-contained `Apeiron.exe` и создаёт:

| Путь | Описание |
|------|----------|
| `bin/Release/net8.0-windows/win-x64/publish/Apeiron.exe` | Portable-лаунчер (~75 МБ) |
| `dist/Apeiron-<version>-win-x64.zip` | Архив для распространения (exe + `RELEASE.txt`) |
| `dist/Apeiron-<version>-win-x64.zip.sha256` | SHA256 для проверки автообновления |

Без тестов (локальная итерация):

```powershell
.\build-release.ps1 -SkipTests
```

### GitHub Release (для мейнтейнеров)

```bash
git tag v1.5.0
git push origin v1.5.0
```

Репозиторий: [Festov/apeiron-launcher](https://github.com/Festov/apeiron-launcher)  
Workflow: `.github/workflows/release.yml`

### Структура папок

```
Apeiron.exe
.minecraft/          # Файлы игры (versions, libraries, assets)
instances/           # Сохранения, моды и конфиг каждой сборки
config/              # settings.json, builds.json, auth.json (шифрование DPAPI)
logs/                # Логи лаунчера и игры
```

### Minecraft ↔ Java

Версия Java подбирается автоматически и устанавливается при необходимости.

| Minecraft | Java |
|-----------|------|
| 26.x | 25 |
| 1.20.5 – 1.21.x | 21 |
| 1.18 – 1.20.4 | 17–21 |
| 1.17.x | 17 |
| 1.16.x и ниже | 8 |

### Настройки

- **RAM** — глобально (до ~75% ОЗУ); своё значение на сборку в редакторе
- **Язык** — авто / English / Русский
- **Только офлайн** — скрыть вход Microsoft
- **Проверять обновления** — при старте и вручную в настройках
