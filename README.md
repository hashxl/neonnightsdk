# NeonNightSDK

NeonNightSDK, created by **hashXL**, is a shared modding library for *Third Crisis: Neon
Nights*. It gives mods a
consistent way to work with the player lifecycle, scenes, scheduled tasks, settings, user
interfaces, world objects, items, animations, and clothing assets.

NeonNightSDK does not add standalone gameplay content. Install it when another mod lists
`neonnightsdk` as a required dependency, or when you want to develop mods using its API.

## Requirements

- Third Crisis: Neon Nights for Windows
- [TCModLoader](../../TCModLoader/README.md) installed in the game directory

## Installation

1. Close the game.
2. Download `NeonNightSDK.zip`.
3. Open the game's installation directory. In Steam, right-click the game and select
   **Manage > Browse local files**.
4. Open the `Mods` directory and create a folder named `neonnightsdk` if it does not exist.
5. Extract the contents of `NeonNightSDK.zip` into that folder.
6. Start the game normally through Steam.

The final layout must be:

```text
Third Crisis Neon Nights/
├── Third Crisis Neon Nights.exe
├── TCModLoader/
└── Mods/
    └── neonnightsdk/
        ├── manifest.json
        └── NeonNightSDK.dll
```

Do not leave the files directly inside `Mods`, and avoid an extra nested directory such as
`Mods/neonnightsdk/neonnightsdk`.

## Verify the installation

After starting the game, open:

```text
TCModLoader/Logs/TCModLoader.log
```

The log should show that `NeonNightSDK` was loaded. Mods that require the SDK should then
load after it. If a dependent mod still reports a missing dependency, confirm that:

- the directory contains both `manifest.json` and `NeonNightSDK.dll`;
- `manifest.json` uses the identifier `neonnightsdk`;
- the installed SDK version satisfies the version required by the mod;
- only one copy of `NeonNightSDK.dll` is installed.

## Updating

Close the game and replace `manifest.json` and `NeonNightSDK.dll` with the files from the
new release. If TCModLoader appears to load an older version, close the game and delete the
contents of `TCModLoader/Cache` before starting it again.

## Removing

Close the game and remove the `Mods/neonnightsdk` directory. Mods that declare NeonNightSDK
as a required dependency will no longer load until the SDK is installed again.

## For mod developers

The SDK targets .NET Framework 4.7.2 and is designed to be loaded once as a shared
TCModLoader mod. Reference `NeonNightSDK.dll` with `<Private>false</Private>`; never bundle a
second copy inside your own mod.

Start with the [developer documentation](docs/README.md). It includes project setup, the
required manifest dependency, a minimal `ITCMod` implementation, build instructions,
troubleshooting, and links to the detailed API guides:

- [Cheat sheet](docs/CHEATSHEET.md)
- [Core lifecycle and scheduler](docs/NeonNightSDK-Core.md)
- [HudKit user interfaces](docs/NeonNightSDK-HudKit.md)
- [WorldKit objects and interactions](docs/NeonNightSDK-WorldKit.md)
- [AnimationsKit](docs/NeonNightSDK-AnimationsKit.md)
- [WeaponsKit](docs/NeonNightSDK-WeaponsKit.md)
- [Mesh skin packages](docs/NeonNightSDK-MeshSkinPackages.md)
