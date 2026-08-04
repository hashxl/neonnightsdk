# Mesh skin packages (portrait/chibi)

> API reference for `MeshSkinPackageAdapter`. For the end-to-end workflow, the rig-alignment
> failure (outfit equips but renders nothing) and the remap tool, see the wiki page
> [Custom Outfit Meshes](../../../ThirdCrisisModding.wiki/Custom-Outfit-Meshes.md).

`MeshSkinPackageAdapter` imports the output of `TCNN-Mod-Mesh-Editor` directly.
The package folder contains:

- `single.json`: weighted Spine mesh geometry, triangles and normalized page UVs;
- the PNG named by `single.json.page`;
- `outfit.json`: item metadata and target skeleton;
- `hide.json` (optional): slots from the `naked` skin to make transparent;
- `icon.png` (optional): inventory icon.

The adapter registers an `Equipment` keyed `tcnn_<id>`, assigns `tcnn/<id>` to
the correct item skin list, and injects the mesh when the player's skeleton
becomes available. It retries automatically on `SdkEvents.OnSceneReady`.

## Automatic discovery (no code)

The SDK sweeps `Mods/*/assets/*` during its own `OnModLoaded` and registers
every package it finds. **An outfit mod does not have to call anything** — ship
the package folder under your mod's `assets\` and the item exists.

Because this runs before any dependent mod's `OnModLoaded`, a shop that lists
clothing by item key (Virtual Atelier, for one) finds `tcnn_<id>` already in
`Item.All` instead of racing the outfit's owner to register it.

Register a single package explicitly when you want control over the surface, or
when the package lives outside `assets\`:

```csharp
MeshSkinPackageAdapter.LoadPackage(manifest, "assets/racer");
MeshSkinPackageAdapter.LoadPackageFrom(@"C:\...\Mods\Foo\assets\racer");
```

Both are idempotent: a folder already registered is applied, not duplicated.

## Split packages (one folder per surface)

A package may keep `single.json` in the package folder, or split it into one
subfolder per exported surface — the mesh editor exports overworld and portrait
separately, each with its own page PNG and `hide.json`, sharing one
`outfit.json` at the package root:

```
assets/racer/
  outfit.json          <- id, name, icon, slots
  icon.png
  overworld/  single.json + its page + hide.json
  portrait/   single.json + its page + hide.json
```

Both layouts produce one item and one `tcnn/<id>` skin.

## Picking the rig a surface is built on

Every surface is offered to every rig the **player** uses — never to NPCs. Two guards decide
whether it is actually built there:

1. **`"rig"` in `single.json`** (written by `remap_to_live_rig.py`) pins the surface to one
   `SkeletonDataAsset` name. When present, nothing else is even attempted.
2. **Exact-match gate.** Before any donor substitution, the adapter counts meshes matching the
   authored skin + attachment + slot. Zero hits means the surface belongs to another skeleton and
   it is skipped, with one log line rather than a warning per mesh.

The gate matters because a donor lookup returns whatever mesh sits on a slot number. Once a
package has been remapped, every index is in range on both rigs, and without the gate the portrait
surface would "match" the chibi rig and drape the character in unrelated geometry.

When the authored source skin no longer exists on the rig, the adapter clones any mesh attachment
on the same slot instead — the source is only a template, since its geometry, UVs and triangles
are all overwritten from the package.

## When nothing matches

The adapter writes the live rig's slot, bone and skin names to
`Mods/NeonNightSDK/rig-dumps/<rig>.json` the first time a package fails to match it. Feed that to
`TCNN-Mod-Mesh-Editor/remap_to_live_rig.py` to re-point the package without redrawing anything.

## Selecting portrait or chibi

Automatic selection uses `outfit.json.skeleton`:

- a name containing `Portrait` selects `DialogueSpineSkins`;
- a name containing `Chibi` or `Overworld` selects `OverworldSpineSkins`;
- otherwise both surfaces are selected.

You can add an explicit `"surface": "Portrait"`, `"surface": "Chibi"` or
`"surface": "Both"` to `outfit.json`, or override it from code:

```csharp
MeshSkinPackageAdapter.LoadPackage(
    manifest, "assets/racer", MeshSkinSurface.Portrait);
```

The `skeleton` value is also used as an exact, case-insensitive asset-name
guard. A package exported for `PortraitZoey` will not be injected into a
different rig whose slot and bone indexes may mean something else. A split
package's root `outfit.json` should therefore leave `skeleton` unset — it spans
both rigs, and pinning it to one would block the other. The hint is read from a
surface's own `outfit.json` only when the package has exactly one surface.

`"importScale"` is optional and defaults to `0.01`, matching TCNN's live Spine
import scale. It scales only the bone-local X/Y values; weights are unchanged.
