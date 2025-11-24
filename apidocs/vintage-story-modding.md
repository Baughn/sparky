# Vintage Story Modding Guide

**Last Updated:** 2025-10-13
**Vintage Story Version:** 1.21.4
**Target Framework:** .NET 8.0

## Overview

Vintage Story code mods are C# libraries that extend the game using the VintageStoryAPI. This project uses a flake-based development environment on NixOS.

## Project Structure

```
underfall/
├── flake.nix              # Nix flake with .NET 8 SDK
├── .envrc                 # Direnv configuration
├── Underfall.csproj       # C# project file
├── modinfo.json           # Mod metadata
├── UnderfallModSystem.cs  # Main mod system class
└── context/               # Documentation
```

## Key Concepts

### ModSystem

All mods require at least one `ModSystem` class that inherits from `Vintagestory.API.Common.ModSystem`. This class serves as the entry point for your mod.

**Key lifecycle methods:**

- `Start(ICoreAPI api)` - Called on both client and server during initialization
- `StartServerSide(ICoreServerAPI api)` - Server-only initialization (world logic, commands)
- `StartClientSide(ICoreClientAPI api)` - Client-only initialization (rendering, UI, input)
- `Dispose()` - Cleanup when mod is unloaded

### Client vs Server

Vintage Story uses a client-server architecture even in single-player:

- **Server side:** Game logic, world generation, entity behavior, commands
- **Client side:** Rendering, UI, input handling, visual effects

Most code mods extend both sides.

## Development Setup

### Prerequisites

1. .NET 8 SDK (provided by flake)
2. Vintage Story installation
3. VintageStoryAPI.dll from your VS installation

### Environment Variables

Set `VINTAGE_STORY` to point to your Vintage Story installation:

```bash
export VINTAGE_STORY=/path/to/vintagestory
```

Add this to your `.envrc` if you want it to persist.

### Building

```bash
dotnet build
```

For a release build with packaging:

```bash
dotnet build -c Release
```

This creates `bin/Underfall.zip` ready to install.

## modinfo.json

Required metadata file for all mods:

```json
{
  "type": "code",           // "code" for C# mods
  "name": "Underfall",      // Display name
  "modid": "underfall",     // Unique identifier (lowercase)
  "version": "0.1.0",       // Semantic version
  "description": "...",     // Short description
  "authors": ["..."],       // List of authors
  "dependencies": {
    "game": "1.19.0"        // Minimum game version
  },
  "side": "universal"       // "server", "client", or "universal"
}
```

## .csproj Configuration

Key elements in `Underfall.csproj`:

```xml
<TargetFramework>net8.0</TargetFramework>
```

Reference to Vintage Story API:

```xml
<Reference Include="VintagestoryAPI">
  <HintPath>$(VINTAGE_STORY)/VintagestoryAPI.dll</HintPath>
  <Private>false</Private>  <!-- Don't copy to output -->
</Reference>
```

## Common Patterns

### Logging

```csharp
api.Logger.Notification("Info message");
api.Logger.Warning("Warning message");
api.Logger.Error("Error message");
api.Logger.Debug("Debug message");
```

### Registering Commands

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    api.RegisterCommand("mycommand", "Description", "",
        (player, groupId, args) => {
            // Command implementation
        });
}
```

### Accessing World Data

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    IWorldAccessor world = api.World;
    // Access blocks, entities, chunks, etc.
}
```

## Resources

- Official Wiki: https://wiki.vintagestory.at/
- Code Mod Tutorial: https://wiki.vintagestory.at/Modding:Code_Mods
- API Documentation: Available in VS installation
- Template Installation: `dotnet new install VintageStory.Mod.Templates`

## Troubleshooting

### VintageStoryAPI.dll not found

Ensure `VINTAGE_STORY` environment variable points to your VS installation directory.

### Build errors

Check that you're targeting the correct .NET version (net8.0 for VS 1.21.x).

### Mod not loading

1. Check modinfo.json syntax
2. Verify mod is in correct Mods folder
3. Check game logs for errors
4. Ensure modid is unique and lowercase

## Next Steps

- Read the Code Tutorial Essentials: https://wiki.vintagestory.at/Modding:Code_Tutorial_Essentials
- Explore example mods: https://github.com/anegostudios/vsmodexamples
- Check out copygirl's example: https://github.com/copygirl/howto-example-mod
