# NeonNightSDK — cheat sheet

Everything the SDK can do, on one page. Each section links to the deep-dive doc when there is
one. Version `v0.3.0`, namespace root `NeonNightSDK`.

Depend on it from your `manifest.json`:

```json
"Requires": { "neonnightsdk": "v0.3.0" }
```

---

## The one thing to start with: `ModContext`

`NeonNightSDK.Core.ModContext` — your mod's handle on the SDK. Everything you subscribe or
schedule through it is unsubscribed/cancelled in one `Dispose()`, so unloading can't leak a
listener into the next session.

```csharp
public class MyMod : ITCMod
{
    private ModContext _ctx;

    public void OnModLoaded(ModManifest manifest)
    {
        _ctx = ModContext.For(manifest);
        _ctx.OnGameplaySceneReady(scene => SpawnMyStuff());
    }

    public void OnModUnLoaded() => _ctx.Dispose();
    public void OnFrame() { }
}
```

| Member | What it does |
|---|---|
| `ModContext.For(manifest)` | Creates it. One per mod |
| `.Manifest` / `.Id` / `.ModPath` | The manifest, the unique id, the mod's folder |
| `.Log(msg)` / `.Warn(msg)` / `.Error(msg)` | Console output tagged with your mod id |
| `.LoadSprite("Assets/x.png")` | Sprite from a path relative to your mod folder |
| `.PathTo("Assets/x.png")` | Absolute path inside your mod folder |
| `.Dispose()` | Drops every handler and task registered through this context |

### Events (all return the context, so they chain)

| Event | Fires |
|---|---|
| `.OnSceneLoaded(name => …)` | Every scene load, immediately |
| `.OnSceneReady(name => …)` | After the scene's transition finishes |
| `.OnGameplaySceneReady(name => …)` | Same, but skips the main menu — the usual one for spawning |
| `.OnPlayerReady(character => …)` | The player character exists and is usable |
| `.OnPlayerLost(character => …)` | The player went away (scene change, teardown) |
| `.OnUpdate(() => …)` | Every frame |
| `.OnDialogueStarted(d => …)` / `.OnLineStarted(l => …)` | Dialogue hooks |
| `.WhenPlayerReady(character => …)` | Fires now if the player already exists, otherwise on the next `OnPlayerReady` |

The same events exist statically on `SdkEvents` if you don't want the auto-cleanup.

### Timing

| Call | What it does |
|---|---|
| `.NextFrame(action)` | Run once, next frame |
| `.After(seconds, action, unscaledTime: false)` | Run once, later |
| `.Every(seconds, action, unscaledTime, catchUp)` | Repeat forever |
| `.Repeat(seconds, times, action)` | Repeat N times |
| `.When(() => condition, action, timeoutSeconds)` | Poll until true, then run once |

All return a `ScheduledTask`: `.Cancel()`, `.Pause()`, `.Resume()`, `.IsDone`,
`.CancelOnSceneChange()`, `.Named("x")`. The same API is on `Scheduler` statically.

---

## Player — `NeonNightSDK.Core`

| Call | What it does |
|---|---|
| `PlayerRef.Current` | The player `Character`, cached per frame. Handles the Unity fake-null trap |
| `PlayerRef.Handler` | The player's `CharacterHandler` (transform, skeleton) |
| `PlayerRef.IsAvailable` | Is there a player right now |
| `PlayerControl.LockMovement(character, id)` / `UnlockMovement` | Freeze/unfreeze movement by named restraint |
| `PlayerControl.LockPlayer(id)` / `UnlockPlayer(id)` | Full input lock (what a modal window uses) |
| `PlayerControl.SetMovementRestraint(character, id, locked)` | Both of the above in one call |

Always namespace restraint ids (`"MyMod.Reason"`) — the restraint set is engine-wide.

## Scenes

| Call | |
|---|---|
| `Scenes.Active` | Current scene name |
| `Scenes.IsGameplay(name)` | Not the main menu |
| `Scenes.IsMainMenu(name)` | |

---

## UI — `NeonNightSDK.Ui` → [HudKit doc](NeonNightSDK-HudKit.md)

Two shapes: a **window** (panel with regions) and an **overlay** (corner HUD).

```csharp
var win = HudKit.Window("Shop", 800, 500, sidebarWidth: 220, footerHeight: 30f);
win.SetBody(b => b
    .Title("Welcome")
    .Text("Anything you like.")
    .Button("Buy", Buy));
```

### `HudKit.Window(title, width, height, …)`

Optional: `sidebarWidth`, `headerHeight`, `footerHeight` (0 = that region doesn't exist),
`modal`, `closeButton`, `closeOnEscape`, `theme`, `id`, `persistAcrossScenes`.

Returns a `UiWindow`: `.SetBody(…)` `.SetSidebar(…)` `.SetHeader(…)` `.SetFooter(…)`
`.ScrollBodyToTop()` `.Close()` `.IsOpen` `.Closed` event. `SetBody` is the navigation
primitive — one call per "page".

### `HudKit.Overlay(name, corner)`

A non-blocking corner HUD. `.StatIcon(sprite, () => value01)` adds an icon whose fill follows a
0–1 value, `.CriticalThreshold`, `.Refresh()`, `.Show()`, `.Hide()`, `.Destroy()`.

> Build overlays inside your first `OnSceneLoaded`, not in `OnModLoaded` — the canvas is
> `DontDestroyOnLoad` and one created in the bootstrap scene is destroyed on the first
> transition and never comes back.

### `UiBuilder` — the widgets

| Widget | |
|---|---|
| `.Title(text)` `.Heading(text)` `.Text(text)` `.Muted(text)` | Text at four sizes/tones |
| `.Button(label, onClick, width)` | Standard button |
| `.Link(label, onClick)` | Hyperlink-looking button |
| `.Input(placeholder, onSubmit, width, configure)` | Single-line field, submits on Enter |
| `.Image(sprite, height, width)` | Aspect-preserving image |
| `.Separator()` `.Spacer(height)` `.Flexible()` | Divider, gap, "eat remaining space" |
| `.Row(b => …, height)` `.Column(b => …, width)` | Nesting; `Flexible()` inside a Row pushes things apart |
| `.Custom(name, rect => …, width, height)` | Escape hatch: a raw `RectTransform` the layout still positions |
| `.Clear()` | Empty the container |

`UiTheme` is a plain object of colours/sizes — `UiTheme.Default.Clone()`, change fields, pass as
`theme:`.

---

## World — `NeonNightSDK.World` → [WorldKit doc](NeonNightSDK-WorldKit.md)

| Call | What it does |
|---|---|
| `WorldKit.SpawnInteractable(sprite, position, onInteract, …)` | A whole interactable object: sprite + collider + `Interactable`. Idempotent by `name` |
| `WorldKit.AttachToExisting(nameContains, onInteract, …)` | Makes objects already in the scene interactable, matched by name. Returns how many |
| `WorldKit.AttachInteractable(gameObject, onInteract, …)` | Same, for a specific object |
| `WorldKit.CreateTrigger(position, size, onEnter, onlyPlayer: true)` | An invisible trigger volume |

Common options: `type` (`InteractionType.Talk`, …), `maxDistance`, `colliderSize`, `iconOffset`,
`sortingLayer`/`sortingOrder`, `worldHeight`.

> `Interactable` needs a **3D** `Collider`. These calls guarantee one — a hand-added
> `BoxCollider2D` silently fails at runtime.

---

## Items — `NeonNightSDK.Items`

| Call | |
|---|---|
| `ItemsKit.RegisterConsumable(key, name, description, sprite, healAmount)` | A usable item |
| `ItemsKit.RegisterKeyItem(key, name, description, sprite)` | A quest/key item |
| `ItemsKit.RegisterBlankEquipment(…)` | Equipment with no visuals of its own |

### Weapon action buttons → [WeaponsKit doc](NeonNightSDK-WeaponsKit.md)

| Call | |
|---|---|
| `WeaponsKit.AddAction(weapon, index, name, icon, callback, hotkey, cooldown, …)` | Adds a button to the `WeaponUI` action bar |
| `WeaponsKit.RemoveAction(weapon, index)` | Removes it |

Index 0/1/2 auto-bind to the game's `Use`/`Cancel`/`Tool_TertiaryAction` hotkeys.
`WeaponUI.Create` draws one button per `Weapon.Actions` entry — so "adding a button" is
"adding an action", there is no prefab involved.

---

## Animations — `NeonNightSDK.Animations` → [AnimationsKit doc](NeonNightSDK-AnimationsKit.md)

For **one-shot** animations (an action, a cutscene beat). Continuous movement animation is
driven by the game's own `OverworldAnimationTranslator` and is not this kit's job.

| Call | |
|---|---|
| `AnimationsKit.PlayAnimationPipeline(skeleton, steps, onFinished)` | Chain of `AnimationPipelineStep`s played in order |
| `AnimationsKit.PlayAnimationPipelineForCharacter(character, steps, …)` | Same, on every handler of a character |
| `AnimationsKit.RegisterBoneRotationAnimation(skeletonData, name, tracks)` | Build a Spine animation from bone-rotation keyframes at runtime |
| `AnimationsKit.RegisterBoneRotationAnimationFromJson(skeletonData, manifest, path)` | Same, from a JSON file |
| `AnimationsKit.RegisterBoneRotationAnimationForCharacter(character, name, tracks)` | Same, applied per character |

`new AnimationPipelineStep("actions/interactions/pickup", loop: false, durationOverride: 0.5f)`

## Clothing / Spine — `NeonNightSDK.Clothing`

| Call | |
|---|---|
| `ClothingKit.RegisterRigidClothingSkin(…)` | New skin from a flat sprite pinned to one slot |
| `ClothingKit.RegisterRemappedClothingSkin(…)` | New skin reusing an existing skin's attachment geometry (deforms with the body) |
| `…ForCharacter(character, …)` | The same two, applied to a loaded character |
| `ClothingKit.BuildRigidAttachment(sprite, material, name)` | Lower level: one `RegionAttachment` |
| `ClothingKit.ApplyTransform(region, x, y, w, h, rotation)` | Position/scale an attachment |

### JSON-driven imports — `NeonNightSDK.Loader`

| Call | |
|---|---|
| `AssetImportLoader.LoadFolder(manifest, "MyClothes")` | Reads `*.json` + PNGs and registers clothing with no code |
| `AssetImportLoader.ApplyToCharacter(character)` | Applies everything imported to a character |

---

## Utility

| Call | |
|---|---|
| `CharacterSkeletons.Get(handler)` | The `SkeletonAnimation` of a handler |
| `CharacterSkeletons.GetAll(character)` | Every skeleton of a character |
| `new WeightedDropTable<T>()` + `.Add(value, weight)` + `.Roll()` | Weighted random pick |

## Console commands the SDK adds

| Command | |
|---|---|
| `nnsdk.dump.inventory <character>` | Lists a character's items with their real types — the fastest way to learn an item's key and class |

---

## Gotchas worth knowing up front

- **Bootstrap-scene trap.** Anything `DontDestroyOnLoad` created during `OnModLoaded` is
  destroyed on the first transition to `MainMenu`. Defer creation to the first
  `OnSceneLoaded`/`OnGameplaySceneReady`.
- **`Interactable` needs a 3D collider.** Use `WorldKit`; a `BoxCollider2D` throws at runtime.
- **`PlayerRef.Current`, not `Character.Get("Zoey")`.** The latter is ambiguous and hits Unity's
  fake-null trap.
- **Namespace every id.** Restraint ids, modifier ids, window ids — they all live in
  engine-wide dictionaries shared with the base game and other mods.
- **Reference the SDK with `<Private>false</Private>`.** TCModLoader loads `NeonNightSDK.dll`
  once as its own mod; bundling a second copy breaks type identity.
