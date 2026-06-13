# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SyncClipboard is a cross-platform clipboard synchronization tool (Windows/macOS/Linux) with clipboard history management. It supports a standalone ASP.NET Core server, client-built-in server, WebDAV, or S3-compatible storage as the sync backend. Desktop clients use either Avalonia (cross-platform) or WinUI3 (Windows-native) for UI.

## Build & Test Commands

This is a multi-platform .NET solution. The sln cannot be built as a whole (it contains platform-specific projects that fail on incompatible OS/arch). All projects use [central package management](https://learn.microsoft.com/en-us/nuget/consume-packages/Central-Package-Management) via `src/Directory.Packages.props`. Commands below are run from the repo root unless noted.

### Windows (WinUI3)

WinUI3 is the primary Windows client (`net9.0-windows10.0.19041.0`). Requires the Windows App SDK — only builds on Windows. The CI uses msbuild, not dotnet CLI.

```bash
# Restore
dotnet restore src/SyncClipboard.WinUI3

# Build (Release, x64 — CI command)
msbuild src\SyncClipboard.WinUI3\SyncClipboard.WinUI3.csproj \
  /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:Configuration=Release \
  /p:WindowsAppSDKSelfContained=true /p:SelfContained=true /v:m -restore
```

For `arm64`, swap `/p:Platform=arm64 /p:RuntimeIdentifier=win-arm64`. Set `/p:SelfContained=false` to skip bundling the .NET runtime; set `/p:WindowsAppSDKSelfContained=false` to skip bundling the Windows App SDK.

### Linux (Avalonia)

Linux uses `SyncClipboard.Desktop.Default` (Avalonia, `net8.0`).

```bash
dotnet build src/SyncClipboard.Desktop.Default/SyncClipboard.Desktop.Default.csproj
```

### macOS (Avalonia)

macOS uses `SyncClipboard.Desktop.MacOS` (Avalonia, `net9.0-macos`). Requires the `macos` workload and only builds on macOS. CI runs from the project directory.

```bash
# Restore (from src/SyncClipboard.Desktop.MacOS/)
dotnet restore src/SyncClipboard.Desktop.MacOS

# Publish (CI command, from src/SyncClipboard.Desktop.MacOS/)
dotnet publish src/SyncClipboard.Desktop.MacOS/SyncClipboard.Desktop.MacOS.csproj \
  -r osx-x64 -c Release
```

Replace `osx-x64` with `osx-arm64` for Apple Silicon.

### Server

The standalone server is cross-platform `net8.0`.

```bash
# Restore
dotnet restore src/SyncClipboard.Server

# Build (Debug)
dotnet build src/SyncClipboard.Server

# Publish (CI command)
dotnet publish src/SyncClipboard.Server/SyncClipboard.Server.csproj \
  --configuration Release --no-restore
```

### Shared / Core Libraries

These are platform-agnostic and build anywhere:

```bash
dotnet build src/SyncClipboard.Shared
dotnet build src/SyncClipboard.Core
dotnet build src/SyncClipboard.Server.Core
dotnet build src/SyncClipboard.Desktop
```

### Tests

```bash
# Run all tests
dotnet test src/SyncClipboard.Test
dotnet test src/SyncClipboard.Test.Desktop
dotnet test src/SyncClipboard.Test.WinUI3   # Windows only

# Run a single test
dotnet test src/SyncClipboard.Test --filter "FullyQualifiedName~TestClassName"
```

Tests use MSTest with Moq. `ServiceProviderDataSource` attributes drive DI validation tests that ensure all registered services can be resolved.

### Code Style

```bash
# Check formatting (from src/ directory)
dotnet format --verify-no-changes --severity info --no-restore
```

Rules are defined in `src/.editorconfig`.

## Architecture

### Project Dependency Graph

```
SyncClipboard.Shared          — Profile models, IProfileEnv, utilities (no heavy deps)
    ↑
SyncClipboard.Server.Core     — ASP.NET Core library: controllers, SignalR hub, EF Core SQLite history, basic auth
    ↑              ↑
SyncClipboard.Core ───────────┘  — Core business logic: services, remote server adapters, ViewModels, DI setup (AppCore)
    ↑
SyncClipboard.Desktop         — Shared Avalonia desktop UI: clipboard factory, views, tray icon, hotkeys
    ↑
    ├── SyncClipboard.Desktop.Default   — Windows/Linux desktop executable (Avalonia, net8.0)
    ├── SyncClipboard.Desktop.MacOS     — macOS desktop executable (Avalonia, net9.0-macos)
    └── SyncClipboard.WinUI3            — WinUI3 native Windows executable (net9.0-windows10.0.19041.0)

SyncClipboard.Server           — Standalone server executable (wraps SyncClipboard.Server.Core)
```

### Core Concepts

**Profile Model (`SyncClipboard.Shared`):** Abstract `Profile` is the unified clipboard content representation. Subtypes: `TextProfile`, `ImageProfile`, `FileProfile`, `GroupProfile`. Every profile has a lazily-computed hash used for deduplication. Profiles can be serialized to `ProfileDto` for wire transfer and `ProfilePersistentInfo`/`ProfileLocalInfo` for storage.

**Service Pattern (`SyncClipboard.Core`):** Every runnable feature implements `IService` (abstract base: `Service`). `ServiceManager` orchestrates all services through their lifecycle: `Start()` → `RegistEventHandler()` → `Load()` (on config change) → `Stop()`. Register new services in `AppCore.ConfigurateUserService()`.

**Clipboard Pipeline:** `UploadService` and `DownloadService` coordinate via named messenger events (`PUSH_START`, `PUSH_STOP`, `PULL_START`, `PULL_STOP`). They use a `remoteProfilemutex` semaphore and `LocalClipboard.Semaphore` to prevent races. `UploadService` extends `ClipboardHander` (which extends `Service`), adding clipboard monitoring and content-control validation.

**Server Adapters (`SyncClipboard.Core/RemoteServer/`):** `RemoteClipboardServerFactory` creates the appropriate `IServerAdapter` based on the active account config. Three adapters exist: `WebDavAdapter` (HTTP/WebDAV), `OfficialAdapter` (SignalR-based real-time sync), and `S3Adapter` (S3-compatible object storage). Register adapters with the `AddServerAdapter<TConfig, TAdapter>()` extension.

**Server Side (`SyncClipboard.Server.Core`):** `SyncClipboardController` handles the WebDAV-compatible API (`GET/PUT SyncClipboard.json` and `file/{name}`) plus dedicated endpoints. `SyncClipboardHub` (SignalR) pushes real-time profile changes to connected clients. History is stored in SQLite via EF Core (`HistoryDbContext`), managed by `HistoryService`. Authentication uses a custom `BasicAuthenticationHandler` with either `FileCredentialChecker` (reads from appsettings.json) or `StaticCredentialChecker` (env vars).

### DI Wiring

`AppCore.ConfigCommonService()` in `SyncClipboard.Core` registers the full service graph: logging, configuration, clipboards, adapters, managers, and infrastructure. Platform-specific registrations happen in `AppServices.ConfigDesktopCommonService()` in `SyncClipboard.Desktop`, which calls `ConfigCommonService()` then adds platform-specific clipboard readers, hotkey registries, and window implementations. On Linux, additional clipboard readers (`XClipReader`, `WlClipboardReader`) and position providers are registered.

### Platform Abstraction

Compile-time constants (`WINDOWS`, `MACOS`, `LINUX`) are defined via `RuntimeIdentifier` checks in `SyncClipboard.Core.csproj`. Platform-specific code uses `#if` guards or `OperatingSystem.Is*()` runtime checks. Desktop clipboard implementations live in `SyncClipboard.Desktop/ClipboardAva/`, with Linux-specific readers in `ClipboardAva/ClipboardReader/`.

### Configuration

`ConfigManager` handles JSON-based configuration files. User-level config types (e.g., `SyncConfig`, `ServerConfig`, `HistoryConfig`) are records in `Models/UserConfigs/`. `AccountManager` manages the list of server connections, each with a type-specific config (`WebDavConfig`, `OfficialConfig`, `S3Config`).

### i18n

Strings are in `SyncClipboard.Core/I18n/Strings.resx` (auto-generated `Strings.Designer.cs`). Supports Chinese and English. Language is set via `I18nHelper.SetProgramLanguage()` from the `ProgramConfig.Language` setting.

## Code Conventions

- Target framework: primarily `net8.0`, with `net9.0-macos` for the macOS project and `net9.0-windows10.0.19041.0` for WinUI3.
- Nullable reference types enabled project-wide (`<Nullable>enable</Nullable>`).
- Central package management: add/update versions only in `Directory.Packages.props`.
- The project uses MSTest with Moq for mocking. Test data source attributes (`PlatformServiceProviderDataSource`, `SystemServiceProviderDataSource`) drive DI validation tests.
- Commit messages follow conventional format with Chinese description prefixes (e.g., `fix:`, `feat:`).
- `src/.editorconfig` defines C# code style rules.
