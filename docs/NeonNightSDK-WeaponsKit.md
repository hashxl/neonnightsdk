# NeonNightSDK — WeaponsKit (weapon action buttons)

## Resumo

Whenever the player has a weapon drawn, the game shows an action bar: `WeaponUI(Clone)`, a
panel containing `Contents`, a `HorizontalLayoutGroup` that holds one `ActionUI(Clone)` per
usable action on that weapon (fire, reload, aim, whatever the weapon defines). A modder who
wants to add a new action button to a weapon does **not** need to touch any prefab — the game
already builds one button per `Weapon.Action` on its own. `WeaponsKit` exists only to cover the
one case the base game does not handle: a weapon that is **already equipped**, where the button
bar was already built and needs to be rebuilt on the fly.

## Como funciona

### Where `ActionUI(Clone)` actually comes from

Decompiling `Asuna.Items.Weapon` and `Asuna.Items.WeaponUI` (`Assembly-CSharp.dll`, via
`ilspycmd`) shows the whole mechanism:

```csharp
// Asuna.Items.Weapon
protected virtual void WeaponEquip(Character owner)
{
    ...
    Timer.WaitForFrame(delegate
    {
        ...
        if (owner.IsPlayer)
        {
            InputManager.AddBindDownListener("Cancel", CancelButtonPressed);
            foreach (Action action in Actions)
            {
                action.hotkeyCallback = delegate { UseAction(action); };
                InputManager.AddBindDownListener(action.hotkey, action.hotkeyCallback);
            }
            WeaponUI.Create(this);
        }
    });
}

// Asuna.Items.WeaponUI
public static void Create(Weapon weapon)
{
    if ((bool)instance) Remove();
    instance = Object.Instantiate(prefab).GetComponent<WeaponUI>(); // Resources.Load("UI/WeaponUI")
    foreach (Weapon.Action action in weapon.Actions)
    {
        Object.Instantiate(instance.ButtonPrefab, instance.Contents)
            .GetComponent<WeaponActionUI>()
            .Populate(weapon, action);
    }
    ...
}
```

`WeaponUI.ButtonPrefab` is the `ActionUI` prefab — every instance of it becomes an
`ActionUI(Clone)` under `Contents`. `WeaponActionUI.Populate(weapon, action)` wires the icon
(`action.displayIcon`), label (`action.displayName`), keybind glyph (`action.hotkey`), and the
click handler (`Button.OnRelease` → `weapon.UseAction(action.index)`).

So `weapon.Actions` — a `List<Weapon.Action>` built from the `Weapon`'s internal
`Dictionary<int, Action>` — is the single source of truth for the action bar. Adding a
`Weapon.Action` is the entire feature; there is no prefab to edit, clone, or patch.

### The gap `WeaponsKit` fills

`WeaponUI.Create` only ever runs once, inside `WeaponEquip`, from a snapshot of `Actions` taken
that same frame. Nothing in `` listens to `Weapon.OnActionAdded` /
`Weapon.OnActionRemoved` — those events exist on `Weapon` but nobody subscribes to them. Two
consequences if a mod calls `Weapon.SetAction` directly on a weapon that is already equipped:

1. The new `ActionUI(Clone)` never appears — the on-screen `WeaponUI(Clone)` is stale until the
   player re-equips the weapon (unequip → equip runs `WeaponEquip` again).
2. If a `hotkey` was given, it is never bound to `InputManager` — that binding loop also only
   runs inside `WeaponEquip`.

`WeaponsKit.AddAction` / `RemoveAction` do exactly what `WeaponEquip` does for a single action,
on demand: call `Weapon.SetAction`/`RemoveAction`, and if the weapon is the one currently drawn
by the player, bind/unbind the hotkey and call `WeaponUI.Create(weapon)` again to rebuild the
button bar immediately.

## Arquitetura

| Class | Role |
|---|---|
| `NeonNightSDK.Items.WeaponsKit` | `AddAction` / `RemoveAction` — the SDK surface |
| `Asuna.Items.Weapon` | Owns `Actions` (`Dictionary<int, Action>`); `SetAction`/`RemoveAction`/`GetAction`/`UseAction` |
| `Asuna.Items.Weapon.Action` | Plain data: `displayName`, `displayIcon`, `index`, `hotkey`, `cooldown`, `freezeDuration`, `CanUse`, `callback`, `animCallback`, `hotkeyCallback` |
| `Asuna.Items.WeaponUI` | Builds the `WeaponUI(Clone)` panel and one `ActionUI(Clone)` per action, on `Create(weapon)` |
| `Asuna.Items.WeaponActionUI` | The `ActionUI` component itself — icon/label/keybind/click wiring, lives on `ActionUI(Clone)` |
| `ANToolkit.InputManager` | `AddBindDownListener`/`RemoveBindDownListener` — the hotkey side |

Everything `WeaponsKit` calls into (`Weapon.SetAction`, `WeaponUI.Create`,
`InputManager.AddBindDownListener`, ...) is game code, reached directly because
`NeonNightSDK.csproj` references `Assembly-CSharp.dll`. `WeaponsKit` adds no new
`GameObject`/`Component` of its own — it only drives the game's existing ones correctly for the
"already equipped" case.

## Passo a passo

### 1. Declare the dependency

```json
"Requires": { "neonnightsdk": "v0.3.0" }
```

### 2. Add an action to a weapon — on the LIVE instance, not the `Item.All` template

```csharp
using NeonNightSDK.Items;

WeaponsKit.AddAction(
    pistola,
    index: 2,
    displayName: "Recarregar",
    icon: ctx.LoadSprite("Sprites/reload.png"),
    callback: () => pistola.Reload());
```

`pistola` here **must** be the actual `Weapon` object the player is carrying — never
`Item.All["pistola_padrao"]`. That dictionary holds the template `ScriptableObject`, and
`Weapon._actions` (what `SetAction` writes to) is a plain, non-serialized `Dictionary<int,
Action>`. Unity's `Object.Instantiate` (what `Item.Clone()` calls to hand a player their actual
copy) only carries over **serializable** state; `_actions` is not one, so every clone starts from
its own field initializer — an *empty* dictionary — regardless of what was added to the template
beforehand. Calling `AddAction` on the template mutates an object nobody ever equips; the player
sees nothing. See Limitações below.

- `index` 0/1/2 fall back to the game's own default hotkeys (`"Use"`, `"Cancel"`,
  `"Tool_TertiaryAction"`) if you don't pass `hotkey` — the same rule `Weapon.SetAction` applies
  natively. Any other index with no `hotkey` just gets a clickable button, no keybind — which is
  how the base game treats extra action slots too.
- If `pistola` is not currently equipped by the player, this only stores the action on that
  specific instance — `ActionUI(Clone)` shows up naturally the next time **that same instance**
  is equipped, with zero further SDK involvement.
- If `pistola` **is** currently drawn, the on-screen `WeaponUI(Clone)` is rebuilt in the same
  call and the new `ActionUI(Clone)` appears immediately.

### 3. Remove an action

```csharp
WeaponsKit.RemoveAction(pistola, 2);
```

Unbinds the hotkey (if any was bound) and rebuilds the action bar if the weapon is on screen.

## Exemplos

### A weapon mod that adds an alt-fire button

The reliable way to reach the live instance — whichever clone the player actually ends up
carrying, even across pickups/drops/new saves — is `Equipment.OnEquipmentUsed`: a **static**
`UnityEvent<Equipment>` that fires on every equip/unequip toggle, for every `Equipment` in the
game (`Weapon` included, since it reuses the same `Equipped()` flow). Filtering it by name hands
you the exact object to call `AddAction` on:

```csharp
using Asuna.Items;
using NeonNightSDK.Items;

public class AltFireMod : ITCMod
{
    public void OnModLoaded(ModManifest manifest)
    {
        Equipment.OnEquipmentUsed.AddListener(OnEquipmentUsed);
    }

    private void OnEquipmentUsed(Equipment equipment)
    {
        // Fires on unequip too — IsEquipped already reflects the post-toggle state here.
        if (!equipment.IsEquipped) return;
        if (!(equipment is Weapon pistola) || pistola.name.ToLower() != "pistola_padrao") return;

        WeaponsKit.AddAction(
            pistola,
            index: 3,
            displayName: "Tiro Alternativo",
            icon: BrazzerAssets.Logo(), // ctx.LoadSprite("Sprites/altfire.png") in your own mod
            callback: () => AltFire(pistola),
            hotkey: "Tool_TertiaryAction",
            cooldown: 1.5f,
            canUse: () => pistola.Owner != null && HasAmmo(pistola.Owner));
    }

    public void OnFrame() { }
    public void OnModUnLoaded() => Equipment.OnEquipmentUsed.RemoveListener(OnEquipmentUsed);
}
```

Because this runs at the moment of equipping, `AddAction`'s "is it on screen" check is already
true — the button appears immediately, and `SetAction` is idempotent per `index`, so re-running
it on every equip of the same instance is harmless.

## Limitações

- **Only applies to `Weapon` items.** Plain `Equipment` (not a `Weapon` subclass — e.g. a tool
  like a camera/phone item) never runs `WeaponEquip`/`WeaponUI.Create` at all, no matter what
  `WeaponsKit` does. Confirmed the hard way: `Web_Camera_Phone` turned out to be `Equipment`, not
  `Weapon` (checked with `nnsdk.dump.inventory`), so there is no `ActionUI(Clone)` for it,
  period — `Brazzer`'s phone integration hooks `Equipment.OnEquipmentUsed` directly instead (see
  `Brazzer/Core/PhoneIntegration.cs`), with no `WeaponsKit` involved.
- **`Item.All[key]` is a template, not a live instance — never call `AddAction` on it.**
  `Weapon._actions` is a plain `Dictionary<int, Action>`, not Unity-serializable, and
  `Weapon.Action`'s delegate fields are explicitly `[NonSerialized]`. `Object.Instantiate`
  (what `Item.Clone()` calls) does not carry non-serializable state into the clone — the new
  instance runs its own field initializers instead, i.e. starts with an *empty* `_actions`. An
  action added to the template is invisible to every clone made from it, including the one the
  player equips. Always call `AddAction` on the actual instance in play — see the
  `Equipment.OnEquipmentUsed` pattern in Exemplos.
- **Only weapons the player has equipped get the live rebuild.** `IsOnScreen` requires
  `weapon.IsEquipped && weapon.Owner.IsPlayer`. An NPC's weapon, or a `Weapon` instance sitting
  in inventory, only reflects the change the next time it is equipped by the player — there is
  no on-screen UI to rebuild for anyone else.
- **`index` collisions silently replace.** `Weapon.SetAction` calls `RemoveAction(index)` first;
  reusing an index a base weapon already defines (0 = Use, 1 = Cancel, 2 = tertiary, by
  convention) replaces that action instead of adding a fourth button.
- **Rebuilding calls `WeaponUI.Create`, which destroys and reinstantiates the whole panel** —
  every existing `ActionUI(Clone)` (not just the new one) is recreated. This matches what the
  game itself does on every weapon equip, so it isn't a new cost, but don't call `AddAction` in
  a tight loop.
- **`animationsOnFire`, `IsHighlighted` and the Spine `animCallback` are not exposed** by
  `AddAction`. Those are set directly on the returned `Weapon.Action` if a mod needs them:
  `action.animationsOnFire = new List<string> { "Reload" };`.
- **Not verified at runtime** — confirmed by decompilation of `Weapon`, `WeaponUI` and
  `WeaponActionUI`, but not yet exercised against a live save with an equipped weapon.

## Boas práticas

- Prefer adding actions in `WhenPlayerReady`/`OnEquipped`-style hooks rather than at `OnModLoaded`
  time, since the `Weapon` instance itself (an `Item.All` entry) usually only exists once the
  game has finished loading its item catalog.
- Give every custom action an `index` your mod owns exclusively (4+, if the weapon's base
  actions use 0–2) so you never accidentally clobber a base action.
- Use `canUse` instead of hiding/disabling the button by hand — `WeaponActionUI.Update()` already
  polls `Weapon.CanUseAction` every frame and fades the button in/out on its own.

## Referências

- Code: `neonnightsdk/Items/WeaponsKit.cs`
- Game code (decompiled for this doc via `ilspycmd -t <type> Assembly-CSharp.dll`):
  `Asuna.Items.Weapon`, `Asuna.Items.Weapon+Action`, `Asuna.Items.WeaponUI`,
  `Asuna.Items.WeaponActionUI`, `ANToolkit.InputManager`
- Related: `neonnightsdk/Items/ItemsKit.cs` (no dedicated doc page yet) for registering the items
  a weapon is built from; [NeonNightSDK HudKit](NeonNightSDK-HudKit.md) for custom
  (non-`WeaponUI`) windows and overlays — HudKit does not touch `WeaponUI(Clone)`, it builds
  separate canvases.

## Atualizações

- **v0.3.0** — First release of `WeaponsKit`: `AddAction`/`RemoveAction`, covering the
  already-equipped-weapon case that `Asuna.Items.Weapon`/`WeaponUI` don't handle on their own.
  Corrected after `Brazzer`'s phone integration surfaced two mistakes in this same doc's first
  draft: the examples called `AddAction` on the `Item.All` template (never propagates to the
  clone the player equips — see Limitações) and assumed `Web_Camera_Phone` was a `Weapon` when
  it is plain `Equipment` (not every equippable item goes through `WeaponUI` at all).
