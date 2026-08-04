# NeonNightSDK — WorldKit

## Resumo

Creating an interactable object in the world (a shop NPC, a vending machine, an examinable
prop) means assembling a stack of components by hand: a root `GameObject`, a child for the
visual, a `SpriteRenderer` with a sorting layer, a scale computed from the sprite bounds, a
trigger collider, and finally the `Interactable` with its events wired. That is roughly 40
lines per object, and it was copy-pasted into three separate TestMod services.

`WorldKit` reduces it to one call and — more importantly — guarantees the collider rule
`Interactable` requires, which is easy to violate without noticing (see
[Limitações](#limitações)).

Related: the modding wiki's Interactables, Triggers and NPCs pages document the raw game API.
This page documents the SDK layer on top of them.

## Como funciona

### The 3D collider rule

This is the non-obvious part, and the main reason `WorldKit` exists.

`ANToolkit.Controllers.Interactable` collects its colliders like this:

```csharp
_myColliders.Clear();
_myColliders.AddRange(GetComponentsInChildren<Collider>());
```

and then positions the interaction icon like this:

```csharp
private Vector3 GetIconDesiredLocation()
{
    Bounds finalBounds = _myColliders.First().bounds;   // <-- throws on an empty list
    ...
}
```

Three consequences:

1. A **3D** `UnityEngine.Collider` is required. A `Collider2D` does **not** count —
   `GetComponentsInChildren<Collider>()` simply cannot see it.
2. With no 3D collider at all, `_myColliders.First()` throws `InvalidOperationException` the
   moment the icon tries to position itself. It is not an error at creation time; it is a
   delayed exception that surfaces when the player walks up to the object.
3. The collider may live on a **child** — `GetComponentsInChildren` includes the hierarchy.

`Interactable`'s own `Reset()` adds a `BoxCollider` with `isTrigger = true` when none exists,
but `Reset()` only runs in the Unity **editor**, when the component is added through the
inspector. At runtime, via `AddComponent<Interactable>()`, it is never called.

Every `WorldKit` method checks for and guarantees that collider before adding the
`Interactable`.

### Visual on a child object

The sprite has to be scaled to reach the desired world height. If that scale were applied to
the root, the collider would scale with it, and the interaction volume would silently depend
on the image's pixel resolution. So the `SpriteRenderer` lives on a child named `Visual` and
the root stays at scale 1.

### Idempotence

`SpawnInteractable` looks for an active object with the same name before creating one. If it
exists, it returns the existing object and creates nothing. `AttachToExisting` skips objects
that already carry an `Interactable`.

This removes the `private bool _spawned;` flag every service carried to avoid duplicating
objects when the spawn was called more than once per scene.

## Arquitetura

| Component | Role |
|---|---|
| Root `GameObject` | World position, `BoxCollider` (trigger) and `Interactable` |
| `Visual` child | `SpriteRenderer` plus the scale computed from the sprite |
| `BoxCollider` | The 3D volume `Interactable` requires; `isTrigger = true` so it does not block movement |
| `Interactable` | Icon, max distance, `OnInteracted` event |
| `ANToolkit.Level.Trigger` | Invisible zone for `CreateTrigger`; 3D physics, `OnTriggerEnter(Collider)` |

Objects created by `WorldKit` are ordinary scene objects: Unity destroys them on scene change.
**There is nothing to clean up in `OnModUnLoaded`** — just call the spawn again on the next
scene.

## Passo a passo

### 1. Declare the dependency

```json
"Requires": { "neonnightsdk": "v0.2.0" }
```

### 2. Spawn when the scene is ready

```csharp
using NeonNightSDK.Core;
using NeonNightSDK.World;

public void OnModLoaded(ModManifest manifest)
{
    var ctx = ModContext.For(manifest);

    ctx.OnGameplaySceneReady(scene =>
    {
        if (scene != "NC_Z2_Residential_District") return;

        WorldKit.SpawnInteractable(
            sprite: ctx.LoadSprite("Assets/shopkeeper.png"),
            position: new Vector3(12f, 3f, 0f),
            onInteract: () => _catalogue.OpenShop(),
            name: "MyMod_Shopkeeper");
    });
}
```

Use `OnGameplaySceneReady` (not `OnSceneLoaded`) whenever the interaction may open a dialogue
or play an animation — see [NeonNightSDK Core](NeonNightSDK-Core.md) for why.

## Exemplos

### Shop NPC

```csharp
WorldKit.SpawnInteractable(
    sprite: ctx.LoadSprite("Assets/character.png"),
    position: new Vector3(12f, 3f, 0f),
    onInteract: () => catalogue.OpenShop(),
    name: "MyMod_Shopkeeper");
```

### Scenery with no interaction

```csharp
// The machine is decoration; the vendor standing next to it does the talking.
WorldKit.SpawnProp(
    sprite: ctx.LoadSprite("Assets/vending-shop.png"),
    position: new Vector3(19.61f, -44.66f, 0f),
    name: "MyMod_VendingShop",
    worldHeight: 2.8f);

WorldKit.SpawnInteractable(
    sprite: ctx.LoadSprite("Assets/vendor.png"),
    position: new Vector3(21.61f, -44.66f, 0f),
    onInteract: () => store.Open(),
    name: "MyMod_Vendor");
```

`SpawnProp` builds the same root + `Visual` child + scaling as `SpawnInteractable`, minus the
collider and the `Interactable` — so the player sees the object but gets no interaction icon on
it. Same idempotence by `name`.

### Giving behaviour to an object the game already places

```csharp
// Vending machine the game positions in the level itself.
WorldKit.AttachToExisting("CondomVendingMachine", onInteract: Buy);
```

Matches on `nameContains` (substring, case-sensitive). Returns how many objects were wired up
— `0` is normal and simply means the current scene has none.

### Examinable prop with a different icon

```csharp
WorldKit.SpawnInteractable(
    sprite: ctx.LoadSprite("Assets/poster.png"),
    position: pos,
    onInteract: () => Notification.Create("A faded poster."),
    type: InteractionType.Examine,
    worldHeight: 1.2f,
    name: "MyMod_Poster");
```

Available `InteractionType` values: `Generic`, `Grab`, `Talk`, `Examine`, `Button`, `Door`,
`Exit`, `Locked`. It controls only the **icon** shown.

### Trigger zone

```csharp
WorldKit.CreateTrigger(
    position: new Vector3(4f, 0f, 0f),
    size: new Vector3(2f, 2f, 1f),
    onEnter: _ => Notification.Create("You stepped into the alley."),
    name: "MyMod_AlleyTrigger",
    once: true);
```

`once: true` binds `OnFirstEnter` instead of `OnEnter`, so the callback runs a single time no
matter how often the player re-enters — the usual want for a story beat.

### Reaching the full `Interactable`

The signature exposes what is needed 95% of the time. For the rest, take the component:

```csharp
var go = WorldKit.SpawnInteractable(sprite, pos, onInteract: Open, name: "MyMod_Door");
var interactable = go.GetComponent<Interactable>();

interactable.RequireLineOfSight = true;
interactable.LookAtInteracter = false;
interactable.OnInteractionFinished.AddListener(ctrl => Debug.Log($"{ctrl.name} finished"));
```

`OnInteracted` hands over the `CharController` that interacted. Since most callers do not need
it, the ergonomic signature takes a plain `Action`; add your own listener to the returned
`Interactable` when you do need the controller.

### Sorting order is derived from Y, not chosen

`sortingOrder` in this game is **not** a free number — `ANToolkit.Level.SpriteOrderHelper`
computes it from the object's own position:

```csharp
renderer.sortingOrder = -(int)(renderer.transform.position.y * 100f - offset);
```

Lower on screen (more negative Y) means drawn in front. Consequences for modded props:

- **Never copy another prop's `sortingOrder`.** It encodes *that* prop's Y. Pasting a cabinet
  at `y ≈ 3.15` (order `-315`) onto a machine at `y = -47.89` puts the machine at the wrong
  depth — in practice, behind the background, which looks exactly like it never spawned.
  Copying the sorting **layer** and the material from a native prop is fine; the order is not.
- `WorldKit`'s default order (`100`) is a neutral value for NPCs, not a correct depth for
  scenery placed among level geometry.
- To let the game keep the prop sorted, add its own component to the **renderer's** GameObject
  (the `Visual` child, not the root): `visual.AddComponent<OrderRenderer>()`. It re-runs
  `SpriteOrderHelper` whenever the Y changes. Setting the order once at spawn as well avoids a
  one-frame flash before `OrderRenderer`'s first `Update`.
- `SpriteOrderHelper.DoOrderRenderers()` exists but is only wired to `Entity.OnEntityEnabled`,
  so it will not fix a modded object that is not an `Entity`.

## Limitações

- **A 3D collider is mandatory.** `WorldKit` guarantees it, but if you assemble an
  `Interactable` by hand, remember: `Collider2D` does not work, and the failure appears as a
  delayed `InvalidOperationException`, not a creation-time error.
- **`AttachToExisting` is expensive.** It walks every `Transform` in the scene via
  `FindObjectsOfType<Transform>()`. That is acceptable once per scene; do **not** call it in
  `OnFrame` or in a short `Scheduler.Every`.
- **`GameObject.Find` only sees active objects.** `SpawnInteractable`'s idempotence check will
  not find an object that exists but is disabled, and a second one will be created.
- **Names must be unique.** Prefix with your mod's identifier (`MyMod_Shopkeeper`) to avoid
  colliding with game objects or with another mod.
- **`Trigger` is 3D physics.** `OnTriggerEnter(Collider)` requires a `Rigidbody` on at least
  one side; the player's controller provides it. A trigger between two static objects does not
  fire.
- **`MustBeEntirelyInside`.** `Trigger`'s own default is `true`, requiring the player to fit
  completely inside the box, which surprises people with small zones. `WorldKit` flips that
  default to `false`.
- **No persistence.** Objects are recreated per scene; no state is saved. Use `SaveManager`
  (see the modding wiki's SaveKeys page) for anything that must survive.
- **Scope.** `WorldKit` creates objects with a **sprite** visual. Spine-animated NPCs with
  skeletons and expressions are still assembled through `CharacterHandler` — see the wiki's
  NPCs page.
- **Not verified at runtime.** `Trigger` in particular has no proven precedent in this
  codebase; its behaviour here was derived from decompiled code, not from observation.

## Boas práticas

- Call the spawn from `SdkEvents.OnGameplaySceneReady`, not `OnSceneLoaded`, when the
  interaction opens a dialogue or plays an animation.
- Always pass an explicit `name` prefixed with your mod id — that is what enables idempotence
  and prevents collisions.
- Do not hold references to spawned objects across scenes: they are destroyed on the
  transition. Keep the data and recreate.
- Leave `colliderSize` at its default. It is derived from the already-scaled sprite, so it
  tracks what is on screen; the fixed `1.5 x 2.5` box the old code used was the same regardless
  of the image.
- Adjust `maxDistance` rather than enlarging the collider when the goal is "interact from
  further away". The collider defines the bounds and the icon position; the interaction range
  is `MaxDistance`.
- Check `AttachToExisting`'s return value. If you expect 1 and get 0, the name changed or the
  scene is wrong — that is the fastest diagnosis.

## Referências

- Code: `neonnightsdk/World/WorldKit.cs`
- Lifecycle and scheduling: [NeonNightSDK Core](NeonNightSDK-Core.md)
- UI: [NeonNightSDK HudKit](NeonNightSDK-HudKit.md)
- Raw game API: the modding wiki's Interactables, Triggers and NPCs pages
- Game types: `ANToolkit.Controllers.Interactable`, `ANToolkit.Controllers.InteractionType`,
  `ANToolkit.Level.Trigger` (all in `Assembly-CSharp.dll`)
- Original code that motivated the kit: `testmod-master/Shop/ShopService.cs`,
  `testmod-master/Info/InfoNpcService.cs`,
  `testmod-master/VendingMachine/VendingMachineService.cs`

## Atualizações

- **v0.4.1** — Added `SpawnProp`: sprite-only scenery (no collider, no `Interactable`), for the
  "machine is decoration, the NPC beside it sells" pattern used by TestMod's street stores.
- **v0.2.0** — First release of `WorldKit`: `SpawnInteractable`, `AttachToExisting`,
  `AttachInteractable`, `CreateTrigger`. Documented `Interactable`'s 3D collider requirement,
  discovered by decompiling `GetIconDesiredLocation()`.
