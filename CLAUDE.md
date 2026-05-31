# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

Alua is a cross-platform achievement tracking application built with Uno Platform that integrates with Steam, PlayStation Network, Xbox, and RetroAchievements.

## Build Commands

```bash
# Clone with submodules (Sachya is a submodule at external/Sachya)
git clone --recurse-submodules <repo-url>
# Or, if already cloned:
git submodule update --init --recursive

# Restore dependencies
dotnet restore Alua.sln

# Build the app project for a specific framework. NOTE: build the Alua project, not the .sln,
# with an explicit -f. The bundled Sachya submodule targets plain net9.0 and does not recognize
# the "desktop" platform identifier, so `dotnet build Alua.sln -f net10.0-desktop` fails (NETSDK1139).
dotnet build Alua/Alua.csproj -f net10.0-desktop

# Run the application
dotnet run --project Alua/Alua.csproj -f net10.0-desktop

# Run specific platform profiles
dotnet run --project Alua/Alua.csproj --launch-profile "Alua (Desktop)"
dotnet run --project Alua/Alua.csproj --launch-profile "Alua (WebAssembly)"

# Publish for specific platforms
dotnet publish Alua/Alua.csproj -c Release -f net10.0-desktop -r win-x64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net10.0-desktop -r osx-arm64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net10.0-desktop -r linux-x64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net10.0-android -r android-arm64
```

## High-Level Architecture

### Technology Stack
- **Framework**: Uno Platform (Uno.Sdk 6.5.31, pinned in global.json) with .NET 10.0 (targets `net10.0-desktop;net10.0-android`)
- **UI Pattern**: MVVM with CommunityToolkit.Mvvm
- **Dependency Injection**: Built-in .NET DI container
- **Logging**: Serilog with console and file sinks
- **Object Mapping**: AutoMapper

### Key Architectural Components

1. **Achievement Provider System**
   - Interface: `Models/IAchievementProvider.cs` - Generic interface for platform providers
   - Implementations in `Services/Providers/`:
     - `SteamService.cs` - Steam integration using SteamWebAPI2
     - `PSNService.cs` - PlayStation Network integration using Sachya library
     - `XboxService.cs` - Xbox Live integration
     - `RetroAchievementsService.cs` - RetroAchievements platform

2. **MVVM Architecture**
   - ViewModels in `Services/ViewModels/`:
     - `AppVM.cs` - Main application state and game collection management
     - `FirstRunVM.cs` - Initial setup flow
     - `SettingsVM.cs` - User preferences and API key management
   - Views in `UI/` directory use two-way data binding with ViewModels

3. **Cross-Platform Support**
   - Platform-specific code in `Platforms/` directory
   - Shared UI components using XAML
   - Platform detection and conditional compilation

4. **Dependency Structure**
   - Solution references external Sachya library for PSN functionality
   - Central package management via `Directory.Packages.props`
   - Platform-specific build targets in `Directory.Build.targets`

### Configuration Management
- User-provided API keys and secrets (Steam/RA keys, PSN NPSSO, MSAL cache) are stored encrypted via `Services/SecureStorage.cs` — NOT in appsettings. Do not commit keys to appsettings.
- `appsettings.development.json` (embedded) holds only environment/logging config; `appsettings.example.json` documents the optional local override shape
- Settings and the game library are persisted by `SettingsVM` as JSON (`Settings.json` in local app data)

### Key Patterns
- Async/await throughout for API calls
- Observable properties for UI updates
- Generic provider interface for extensibility
- Structured logging with contextual information

## Development Notes

- Primary IDE: JetBrains Rider (as mentioned in README)
- Code style: File-scoped namespaces, nullable reference types enabled
- No test project currently exists in the solution
- macOS builds require special font handling (see Directory.Build.targets)
- Production API keys managed via GitHub secrets in CI/CD
