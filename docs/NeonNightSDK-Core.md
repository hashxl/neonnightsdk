# NeonNightSDK — Core (context, events and scheduler)

## Resumo

`ITCMod` offers three entry points: `OnModLoaded`, `OnFrame` and `OnModUnLoaded`. That does not
cover what a mod actually needs, so every mod re-implemented the same lifecycle by hand:
subscribe to `SceneManager.sceneLoaded`, keep a `_spawned` flag, write
`if (scene.name == "MainMenu") return;`, and accumulate `Time.deltaTime` manually for anything
periodic.

Core solves three problems — lifecycle, events and time — and isolates mods from each other:
**a mod that throws inside a callback no longer takes down other mods' callbacks**.

## Como funciona

### The right moment in a scene

`SceneManager.sceneLoaded` fires the instant the new scene's objects exist, which is still
underneath the game's full-screen loading curtain.

Decompiling `ANToolkit.Level.LevelTransition.LoadSceneCoroutine`, after the load the game still
runs:

```csharp
yield return new WaitForEndOfFrame();   // x4
Timer.Simple(0.25f, delegate
{
    SaveManager.CanSave.Remove("LevelTransition");
    PostTransition.Invoke();            // <-- the real "about to become visible" moment
    loadingAnimator.SetTrigger("Close");
    isVisible = false;
    ...
});
```

Using the wrong hook has a visible symptom: animations and dialogue play while the screen is
still black, the player only sees the tail end — or misses the window to dismiss the popup,
which reads as "stuck, can't move".

That is why `OnSceneReady` is bound to `LevelTransition.PostTransition`.

**Fallback:** scenes that do not go through `LevelTransition` (the initial boot into
`MainMenu`, or a `SceneManager.LoadScene` called directly by another mod) never fire
`PostTransition`. For those, Core watches `LevelTransition.isVisible` (the curtain's own flag)
and fires once it is down. Whichever comes first wins; there is no double dispatch.

### Resolving the player

Two traps, both handled by `PlayerRef`:

1. **`Character.Get("Zoey")` is not reliable.** The game registers more than one `Character`
   with the same display name (for example `Char_NPC_Zoey`, a cameo clone with
   `IsPlayer == false`), and the name lookup can resolve to that NPC instead of the live
   player.

2. **`CharacterHandler.Player?.Character` lets a destroyed object through.** C#
   null-propagation (`?.`) does not respect `UnityEngine.Object`'s overloaded `==` operator. An
   already-destroyed object passes as if alive and only fails later, in a
   `MissingReferenceException` far from the cause. The correct check is an explicit `== null`.

`PlayerRef` also caches per frame. `CharacterHandler.Player` is a property that runs
`Entity.GetPlayer<PlayerController>()` plus a `GetComponent` on every access, and TestMod was
calling it up to three times per frame.

### Keeping an `Update()` alive

A `GameObject` with `DontDestroyOnLoad` created during the game's **bootstrap** scene is
destroyed anyway on the transition to `MainMenu`. That was the root cause of the
"`ITCMod.OnFrame()` doesn't fire" bug: the loader's `MonoBehaviour` died before the first
frame.

`SdkRuntime` creates its `FramePump` **after** a real scene has loaded, **revalidates on every
scene** (recreating it if it died), and also receives a tick through
`NeonNightSDKMod.OnFrame()`. A `Time.frameCount` latch guarantees the two paths never run
anything twice.

### Failure isolation

Mod callbacks all run inside a single shared loop (the loader's `OnFrame` `foreach`, or a
`UnityEvent`'s invocation list). One uncaught exception aborts the whole loop and silently
skips every mod queued behind it.

Every `SdkEvents` handler and every `Scheduler` callback runs in its own `try/catch`, and the
log names the culprit:

```
[NeonNightSDK] SdkEvents.OnSceneReady: handler 'MyMod.ShopService.OnScene' threw
(the other handlers carried on normally): System.NullReferenceException...
```

## Arquitetura

| Class | Role |
|---|---|
| `SdkEvents` | 8 lifecycle events, each handler isolated |
| `Scheduler` | `After` / `Every` / `Repeat` / `When` / `NextFrame` with cancellable handles |
| `ScheduledTask` | Handle: `Cancel`, `Pause`, `Resume`, `CancelOnSceneChange`, `Named` |
| `ModContext` | Per-mod context; events and tasks with lifetime tied to the mod |
| `PlayerRef` | The player, resolved without ambiguity, cached per frame |
| `PlayerControl` | Input restraints (lock/unlock movement) |
| `SdkRuntime` | Frame pump and translation of the game's events |
| `Scenes` | `IsMainMenu` / `IsGameplay` / `Active` |
| `SdkLog` | Tagged logging and `SafeInvoke` |
| `DebugKit` | Diagnostic console commands (`internal`, installed automatically) |

### Diagnostic commands

`DebugKit.Install()` runs as part of `SdkRuntime.Install()`, so every command below is available
the moment any part of the SDK is used — no opt-in step.

| Command | Does |
|---|---|
| `nnsdk.dump.inventory [nome]` | Lists every item a character is carrying (`Inventory` + `EquippedItems`) with its `Item.All` **key** — `item.name.ToLower()`, not the display name. Defaults to the player when no name is given. |

`Item.All` is keyed by the ScriptableObject's `name.ToLower()` (`Asuna.Items.Item.InitializeBaseItems`), which does not always match the display name shown in-game or its casing. Guessing that key wrong fails silently at an `Item.All[key]` lookup — `nnsdk.dump.inventory` exists so nobody has to decompile the game to find it, the way `web_camera_phone` (display name `"Web_Camera_Phone"`) had to be confirmed for `Brazzer`'s `PhoneIntegration`.

### Events

| Event | Fires when | Use for |
|---|---|---|
| `OnSceneLoaded(string)` | Objects exist, curtain still covering | Registering/spawning things the player need not see appear |
| `OnSceneReady(string)` | The scene is about to become visible | Animation, dialogue, notification, cutscene |
| `OnGameplaySceneReady(string)` | Same, skipping `MainMenu` | The common case |
| `OnPlayerReady(Character)` | The player came into existence | Applying stats, clothing, effects |
| `OnPlayerLost(Character)` | The player stopped existing | Clearing your references |
| `OnUpdate()` | Every frame | Input |
| `OnDialogueStarted(Dialogue)` | A dialogue started | Reacting to base-game dialogue |
| `OnLineStarted(DialogueLine)` | A line started | Matching on `line.LineID` |

`OnDialogueStarted` and `OnLineStarted` exist because `ITCMod` hands those hooks only to a
mod's root class. Re-broadcasting them lets any service listen without the main mod class
becoming a call forwarder.

### Scheduler

| Method | Behaviour |
|---|---|
| `NextFrame(action)` | Runs once, on the next tick |
| `After(s, action)` | Runs once, `s` seconds from now |
| `Every(s, action)` | Runs forever, every `s` seconds |
| `Repeat(s, n, action)` | Runs `n` times, then finishes |
| `When(cond, action, timeout)` | Waits for `cond`, runs once |

All of them return a `ScheduledTask`:

```csharp
var task = Scheduler.Every(5f, Patrol).CancelOnSceneChange();
task.Pause();
task.Resume();
task.Cancel();
```

#### ⚠️ `unscaledTime` and the tab menu: the frozen-HUD trap

`After` / `Every` / `Repeat` all default to `unscaledTime: false`, i.e. scaled game time. That is
the right default for *gameplay* (a paused game should not keep ticking need decay), but it is the
wrong default for anything that only **displays** state.

`Asuna.UI.TabMenu` — the inventory/character screen — sets `Time.timeScale = 0f` in its
constructor and back to `1f` in `OnDestroy`. So while the player has the inventory open,
`Time.deltaTime` is `0` and **every scaled-time task stops firing entirely**.

This produced a bug that looked nothing like a scheduling problem: eating food restored hunger
correctly, but the needs HUD kept showing the old value until the player closed the inventory,
which read as "the food didn't work". The stat had changed the instant the item was used; the
`UiOverlay` refresh task simply hadn't run, because it was on scaled time. `HudKit.Overlay` now
polls on `unscaledTime: true` for exactly this reason.

Rule of thumb: **reading and drawing state → `unscaledTime: true`; changing state → leave it
scaled.** Anything that has to keep working while a menu is open (HUD readouts, animations,
elapsed-time counters in a window) needs the unscaled clock.

## Passo a passo

### 1. Declare the dependency

```json
"Requires": { "neonnightsdk": "v0.2.0" }
```

This makes the game warn the player if the SDK is missing, and makes `ModDependencyResolver`
load the SDK **before** your mod, so `SdkRuntime` is already installed when your `OnModLoaded`
runs.

### 2. Create the context and register everything through it

```csharp
using Modding;
using NeonNightSDK.Core;

public class MyMod : ITCMod
{
    private ModContext _ctx;

    public void OnModLoaded(ModManifest manifest)
    {
        _ctx = ModContext.For(manifest);

        _ctx.OnGameplaySceneReady(scene => SpawnNpc(scene))
            .WhenPlayerReady(player => ApplyStats(player))
            .Every(30f, () => TickHunger());
    }

    public void OnModUnLoaded() => _ctx.Dispose();   // one line, cleans everything

    public void OnFrame() { }
    public void OnDialogueStarted(Dialogue d) { }
    public void OnLineStarted(DialogueLine l) { }
}
```

`ModContext` exists because every mod otherwise has to remember to unsubscribe each handler
individually in `OnModUnLoaded`. A forgotten handler keeps running after the mod is unloaded,
referencing dead objects. Registering through the context makes `Dispose()` the only cleanup
line you need.

## Exemplos

### Scheduling

```csharp
Scheduler.Every(1f, () => Tick());
Scheduler.After(4.33f, () => Rob(zoey));
Scheduler.When(() => PlayerRef.IsAvailable, () => Setup(PlayerRef.Current));
```

### Need decay

```csharp
// catchUp: true because decay is accumulation — if the game stalls for five minutes,
// hunger should reflect those five minutes, not a single tick.
_ctx.Every(30f, () => Decay("hunger"), catchUp: true);
```

### Reacting to a specific line

```csharp
_ctx.OnLineStarted(line =>
{
    if (line.LineID == "lio_intro_03")
        UnlockQuest();
});
```

### Locking movement

```csharp
PlayerControl.LockPlayer("MyMod.Cutscene");
// ...
PlayerControl.UnlockPlayer("MyMod.Cutscene");
```

Restraints are keyed by string and they stack — the game itself uses ids like
`PMA_PathfindingMoveTo` and `LevelTransition`. Always pass an id unique to your feature and
always remove the same id, otherwise you either free the player while another system still
wants them locked, or leave them frozen forever.

## Limitações

- **An `Update()` always runs.** Installing the SDK brings up the `FramePump` even if no mod
  uses Core. The per-frame cost is near zero (a `frameCount` check, early returns in
  `Scheduler` and `PollPlayer` when nothing is subscribed), but it is not literally zero.
- **`OnPlayerReady` does not fire per scene.** It fires when the `Character` *instance*
  changes. Since the instance usually survives a scene change, use `OnGameplaySceneReady` for
  per-scene work and compose the two.
- **`catchUp` is capped.** At most 100 overdue runs per frame; the remainder is dropped with a
  warning, so a long freeze does not stall the frame again in a domino effect.
- **A task that throws is cancelled**, not retried — a broken `Every()` would produce thousands
  of log lines per second.
- **The `Scheduler` does not persist.** Tasks disappear when the mod unloads; nothing is saved.
- **`OnSceneReady` may not fire for the very first scene** if `Install()` happens after it has
  already loaded.
- **Scope.** Core covers no persistence, no UI and no dialogue construction. See the modding
  wiki's SaveKeys and Create-a-Dialogue pages.

## Boas práticas

- Register everything through `ModContext` and call `Dispose()` in `OnModUnLoaded`.
- Use `OnGameplaySceneReady` instead of writing the `MainMenu` guard again.
- Use `OnSceneReady` (not `OnSceneLoaded`) for anything the player must actually **see**.
- Always pass `timeoutSeconds` to a `Scheduler.When` whose condition might never become true.
- Enable `catchUp: true` only when the callback represents accumulation. For anything expensive
  or visible (spawning, playing a sound), leave it off or it turns into a burst.
- Use `CancelOnSceneChange()` for tasks that only make sense in the current scene.
- Never resolve the player with `Character.Get(...)`. Use `PlayerRef.Current`.

## Referências

- Code: `neonnightsdk/Core/`
- World objects: [NeonNightSDK WorldKit](NeonNightSDK-WorldKit.md)
- UI: [NeonNightSDK HudKit](NeonNightSDK-HudKit.md)
- Animation: [NeonNightSDK AnimationsKit](NeonNightSDK-AnimationsKit.md)
- Frame pump investigation: `testmod-master/docs/TCModLoader-OnFrame-Nao-Dispara.md`
- Game types: `ANToolkit.Level.LevelTransition`, `Asuna.CharManagement.CharacterHandler`,
  `Asuna.CharManagement.Character`
- Runtime log at `%AppData%\LocalLow\Anduo Games\Third Crisis Neon Nights\Player.log`
  (`BepInEx\LogOutput.log` only reflects loading, not runtime events). Confirmation that Core
  came up: `[NeonNightSDK] Core v0.2.0 installed (events + scheduler active).`

## Atualizações

- **v0.3.0** — Added `DebugKit` and its first command, `nnsdk.dump.inventory` — the "Ferramentas
  de Diagnóstico" item from the SDK roadmap (`nn-sdk.md`).
- **v0.2.0** — First release of Core: `SdkEvents`, `Scheduler`, `ModContext`, `PlayerRef`,
  `PlayerControl`, `SdkRuntime`, `Scenes`, `SdkLog`. Documented the correct `PostTransition`
  timing, the `Character.Get` ambiguity, and the destruction of `DontDestroyOnLoad` objects
  created during bootstrap. `PlayerControl` was extracted from the input-restraint logic that
  was duplicated in `AnimationsKit` and TestMod's `SleepRobberyService`.
