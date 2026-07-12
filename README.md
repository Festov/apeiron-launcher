# Apeiron Launcher

**Portable Minecraft launcher for Windows** — instances, mod loaders, Microsoft sign-in, and a bilingual UI.

[Installation guide](how-install.md) · [Changelog](CHANGELOG.md) · [Architecture](ARCHITECTURE.md) · [Testing](TESTING.md)

---

## English

### What is Apeiron?

Apeiron is a **single-folder, portable** Minecraft launcher built with .NET 8 and WPF. Drop `Apeiron.exe` anywhere on disk — game files, instances, settings, and logs live next to it. No system-wide install, no separate launcher data in `%AppData%`.

The launcher handles the full path from **download → mod loader → Java → launch**: vanilla and modded instances (Fabric, Quilt, Forge, NeoForge), optional Microsoft account login, or offline play with a validated nickname. The interface is available in **English and Russian**.

Current version: **v1.5**

### Features

#### Instances & modding
- Multiple **instances** (builds) with separate saves, mods, and config under `instances/`
- **Vanilla** and modded installs: Fabric, Quilt, Forge, NeoForge
- **Modpack import** from zip (including Curse/Modrinth-style `overrides/`) — button or **drag-and-drop** on the main window
- **Export / import** instances; **full backup** (mods, config, saves)
- **Mod search** in the instance editor; metadata from `fabric.mod.json` / `mods.toml`
- **Recent Minecraft versions** pinned at the top when creating a new instance
- **Per-instance RAM** override or global default in settings

#### Play & install
- One-click **Play** with install-if-needed flow (`LauncherOrchestrator`)
- **Reinstall** instance (clear version + redownload)
- **Download progress** with cancel; **resume** partial downloads (HTTP Range)
- **Manifest cache** for faster repeat installs; **HTTP retry** on transient failures
- **Auto Oracle JDK** install matched to the Minecraft version (silent, background)
- Install failure logs with **Open log** in the status bar

#### Accounts & launch
- **Microsoft sign-in** (WebView2) with DPAPI-encrypted token storage
- **Offline-only mode** — hide Microsoft login in settings
- **Offline nickname** validation before launch (`a-z`, `0-9`, `_`, 3–16 chars)

#### Launcher UX
- **Dark / light theme**
- **Bilingual UI** — auto-detect, English, or Russian
- **CLI**: `--launch <instance name or id>` and `--help`
- **Auto-update** from GitHub Releases with **release notes** in the prompt
- Structured **JSONL event log** (`logs/launcher-events.jsonl`) alongside session logs
- Square, minimal control styling (no rounded corners on shared buttons/inputs)

#### For developers
- **178+ unit tests**, CI on Windows (build + test + publish)
- [ARCHITECTURE.md](ARCHITECTURE.md) — layers, install/launch flow, disk layout
- [how-install.md](how-install.md) — requirements, portable install, build & release

```bash
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
```

---

## Русский

### Что такое Apeiron?

Apeiron — **portable-лаунчер Minecraft для Windows** на .NET 8 / WPF. Положите `Apeiron.exe` в любую папку — файлы игры, сборки, настройки и логи хранятся рядом. Не нужен установщик и отдельные данные в `%AppData%`.

Лаунчер закрывает весь цикл **скачивание → модлоадер → Java → запуск**: ванильные и модовые сборки (Fabric, Quilt, Forge, NeoForge), вход через Microsoft или офлайн-ник с проверкой. Интерфейс на **русском и английском**.

Текущая версия: **v1.5**

### Возможности

#### Сборки и моды
- Несколько **сборок** с отдельными сохранениями, модами и конфигом в `instances/`
- **Ваниль** и модовые установки: Fabric, Quilt, Forge, NeoForge
- **Импорт модпака** из zip (в т.ч. `overrides/`) — кнопка или **перетаскивание** на главное окно
- **Экспорт / импорт** сборок; **полный бэкап** (моды, конфиг, сохранения)
- **Поиск модов** в редакторе сборки; метаданные из `fabric.mod.json` / `mods.toml`
- **Недавние версии MC** вверху списка при создании сборки
- **Свой RAM** на сборку или глобальное значение в настройках

#### Установка и запуск
- **Play** в один клик с установкой при необходимости (`LauncherOrchestrator`)
- **Переустановка** сборки (очистка версии + повторная загрузка)
- **Прогресс загрузки** с отменой; **докачка** частичных файлов (HTTP Range)
- **Кэш манифеста** MC; **повтор HTTP** при сбоях сети
- **Автоустановка Oracle JDK** под версию Minecraft (в фоне)
- Логи ошибок установки и кнопка **Открыть лог** в статус-баре

#### Аккаунты
- **Вход Microsoft** (WebView2), токены шифруются через DPAPI
- Режим **только офлайн** — скрыть вход Microsoft
- Проверка **офлайн-ника** перед запуском (`a-z`, `0-9`, `_`, 3–16 символов)

#### Интерфейс лаунчера
- **Тёмная / светлая** тема
- **Два языка** — авто, English, Русский
- **CLI**: `--launch <имя или id сборки>` и `--help`
- **Автообновление** с GitHub Releases и **release notes** в диалоге
- Структурированный **JSONL-лог** (`logs/launcher-events.jsonl`) и текстовые сессии
- Плоский UI без скруглений у общих кнопок и полей

#### Для разработчиков
- **178+ юнит-тестов**, CI на Windows (сборка + тесты + publish)
- [ARCHITECTURE.md](ARCHITECTURE.md) — слои, install/launch flow, структура на диске
- [how-install.md](how-install.md) — требования, portable-установка, сборка и релиз

```bash
dotnet test Apeiron.Tests/Apeiron.Tests.csproj -c Release
```

---

## Links

| | |
|---|---|
| Install & build | [how-install.md](how-install.md) |
| Manual test checklist | [TESTING.md](TESTING.md) |
| Code layout | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Version history | [CHANGELOG.md](CHANGELOG.md) |
| Releases | [GitHub Releases](https://github.com/Festov/apeiron-launcher/releases) |
