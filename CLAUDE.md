# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

Alua is a cross-platform achievement tracking application built with Uno Platform that integrates with Steam, PlayStation Network, Xbox, and RetroAchievements.

## Build Commands

```bash
# Restore dependencies
dotnet restore Alua.sln

# Build the solution
dotnet build Alua.sln

# Run the application (defaults to WebAssembly profile)
dotnet run --project Alua/Alua.csproj

# Run specific platform profiles
dotnet run --project Alua/Alua.csproj --launch-profile "Alua (Desktop)"
dotnet run --project Alua/Alua.csproj --launch-profile "Alua (WebAssembly)"

# Publish for specific platforms
dotnet publish Alua/Alua.csproj -c Release -f net9.0-desktop -r win-x64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net9.0-desktop -r osx-arm64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net9.0-desktop -r linux-x64 --self-contained
dotnet publish Alua/Alua.csproj -c Release -f net9.0-android -r android-arm64
```

## High-Level Architecture

### Technology Stack
- **Framework**: Uno Platform 6.0.96 with .NET 9.0
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
- API keys and settings stored in `appsettings.json`
- Development overrides in `appsettings.Development.json`
- Settings persistence handled by SettingsVM with JSON serialization

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