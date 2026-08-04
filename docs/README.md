# NeonNightSDK documentation

NeonNightSDK is a shared library for *Third Crisis: Neon Nights* mods. It provides a
safe lifecycle, scene and player events, scheduling, UI builders, settings, world
objects, item helpers, animation tools, and clothing import utilities.

## Requirements

- Third Crisis: Neon Nights
- TCModLoader installed in the game directory
- NeonNightSDK installed as its own mod under `Mods/neonnightsdk`
- .NET SDK capable of building .NET Framework 4.7.2 projects

Do not bundle `NeonNightSDK.dll` inside another mod. TCModLoader must load one shared SDK
instance, or types from the two copies will not be compatible.

## Install the SDK

Place the SDK files in the game directory with this layout:

```text
Third Crisis Neon Nights/
└── Mods/
    └── neonnightsdk/
        ├── manifest.json
        └── NeonNightSDK.dll
```

Launch the game once and check `TCModLoader/Logs/TCModLoader.log`. The log should list
`NeonNightSDK` as loaded before mods that depend on it.

## Create a mod that uses the SDK

### 1. Declare the dependency

Add NeonNightSDK to your mod's `manifest.json`:

```json
{
  "Name": "MyMod",
  "Author": "YourName",
  "Version": "v1.0.0",
  "UniqueIdentifier": "mymod",
  "PathToDLL": "MyMod.dll",
  "Enabled": true,
  "Requires": {
    "neonnightsdk": "v0.4.0"
  }
}
```

`Requires` makes TCModLoader validate the installed SDK version and load it before your
mod. Use a stable, lowercase `UniqueIdentifier`; changing it later can break settings and
dependencies that refer to your mod.

### 2. Reference the assemblies

Create a .NET Framework 4.7.2 project and reference both TCModLoader and NeonNightSDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <AssemblyName>MyMod</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <PropertyGroup>
    <GameDir>..\..</GameDir>
    <ManagedDir>$(GameDir)\Third Crisis Neon Nights_Data\Managed</ManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="TCModLoader">
      <HintPath>$(GameDir)\TCModLoader\Runtime\TCModLoader.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="NeonNightSDK">
      <HintPath>..\neonnightsdk\NeonNightSDK.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>$(ManagedDir)\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ManagedDir)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

Add other Unity or game assemblies only when your code uses their types. Keep game and SDK
references set to `<Private>false</Private>` so they are not copied into your output.

### 3. Create the mod entry point

Implement `ITCMod`, create one `ModContext`, and dispose it when the mod unloads:

```csharp
using Modding;
using NeonNightSDK.Core;
using UnityEngine;

public sealed class MyMod : ITCMod
{
    private ModContext _ctx;

    public void OnModLoaded(ModManifest manifest)
    {
        _ctx = ModContext.For(manifest);

        _ctx.OnGameplaySceneReady(scene =>
        {
            _ctx.Log($"Gameplay scene ready: {scene}");
        });

        _ctx.OnPlayerReady(player =>
        {
            _ctx.Log($"Player ready: {player.Name}");
        });

        _ctx.Every(1f, () => Debug.Log("One second passed."));
    }

    public void OnFrame()
    {
        // Prefer ModContext events and scheduling for most work.
    }

    public void OnModUnLoaded()
    {
        _ctx?.Dispose();
        _ctx = null;
    }
}
```

Register callbacks through `ModContext` whenever possible. Disposing the context removes
those callbacks and scheduled tasks together, which prevents stale handlers after unload.

### 4. Build and install your mod

```powershell
dotnet build .\MyMod.csproj -c Release
Copy-Item .\bin\Release\MyMod.dll .\MyMod.dll -Force
```

The DLL location must match `PathToDLL` in the manifest. Restart the game after replacing a
DLL. If TCModLoader appears to use an older assembly, close the game and clear
`TCModLoader/Cache`.

## Choose the right guide

| Goal | Guide |
|---|---|
| Find common APIs quickly | [Cheat sheet](CHEATSHEET.md) |
| Handle scenes, players, callbacks, and timers | [Core](NeonNightSDK-Core.md) |
| Build windows, overlays, and controls | [HudKit](NeonNightSDK-HudKit.md) |
| Spawn or attach interactive world objects | [WorldKit](NeonNightSDK-WorldKit.md) |
| Play or compose character animations | [AnimationsKit](NeonNightSDK-AnimationsKit.md) |
| Add weapon action buttons | [WeaponsKit](NeonNightSDK-WeaponsKit.md) |
| Import portrait or chibi mesh-skin packages | [Mesh skin packages](NeonNightSDK-MeshSkinPackages.md) |

Start with the [cheat sheet](CHEATSHEET.md) after the minimal mod works. Each detailed guide
explains lifecycle requirements, complete examples, limitations, and the relevant source
files.

## Troubleshooting

- **The SDK or mod does not load:** verify `manifest.json`, `PathToDLL`, and the loader log.
- **A dependency is missing:** install NeonNightSDK as a separate mod and keep the dependency
  id exactly `neonnightsdk`.
- **Types look identical but cannot be cast:** remove bundled copies of `NeonNightSDK.dll` and
  set the project reference to `<Private>false</Private>`.
- **Scene objects are duplicated:** create them from `OnGameplaySceneReady` and use stable,
  mod-prefixed object names. WorldKit creation methods are designed for idempotent setup.
- **A callback survives unload:** register it through `ModContext`, or unsubscribe it manually
  in `OnModUnLoaded`.
- **A UI or timer freezes while a menu is open:** read the `unscaledTime` section in the
  [Core guide](NeonNightSDK-Core.md).
