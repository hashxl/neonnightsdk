# NeonNightSDK — HudKit (UI)

## Overview

Building any UI in this game means assembling uGUI by hand: a `Canvas`, a `CanvasScaler`, a
`GraphicRaycaster`, then an `Image`/`Text` per element, each with its own
`anchorMin`/`anchorMax`/`sizeDelta`/`anchoredPosition` arithmetic. TestMod's `NeedsHud` spends
70 lines on four icons; `InfoWindow` spends 80 on a box with one line of text and a close
button.

That approach does not scale. Because every widget is positioned absolutely, adding one
element means recomputing the offsets of all the others.

`HudKit` provides two shapes that cover what mods actually need:

| | For |
|---|---|
| `HudKit.Window(...)` | Panels with header / sidebar / body / footer — shops, journals, menus, an in-game browser |
| `HudKit.Overlay(...)` | Corner HUDs bound to live values — needs bars, timers, counters |

Layout is delegated to uGUI's own layout system, so widgets stack and the container does the
arithmetic.

## How it works

### Layout instead of coordinates

The kit never sets `anchoredPosition` on a widget. Every container is a
`VerticalLayoutGroup` or `HorizontalLayoutGroup`, and every widget carries a `LayoutElement`
describing its size. Unity then positions everything.

Three consequences worth understanding:

1. **Order is position.** Widgets appear in the order you add them. Inserting one shifts the
   rest automatically.
2. **`Flexible()` absorbs leftover space.** In a `Row`, a flexible spacer is how you push the
   close button to the far right without knowing the window's width.
3. **Text grows its container.** `UnityEngine.UI.Text` implements `ILayoutElement`, so a
   wrapped paragraph reports its own preferred height and the container grows to fit. No
   manual height, no clipping.

### The window skeleton

```
Panel  (VerticalLayoutGroup, fixed size, centered)
├─ Header                    fixed height, HorizontalLayoutGroup
├─ Middle                    flexibleHeight = 1  → absorbs remaining height
│   ├─ Sidebar               fixed width, scrolls
│   └─ Body                  flexibleWidth = 1   → absorbs remaining width, scrolls
└─ Footer                    fixed height
```

Padding and spacing on the panel itself are zero on purpose: the regions sit flush against
each other and each applies its own inner padding. With panel padding, a strip of window
background shows between the header and the body.

### Scrolling

`ScrollRect` requires a specific three-object structure, and getting the anchors wrong is the
most common way hand-rolled Unity UI breaks — content that either refuses to scroll or scrolls
into infinity. `UiFactory.MakeScrollable` builds it once:

```
Region          ScrollRect
└─ Viewport     RectMask2D (clips)
   └─ Content   anchored top-stretch, pivot (0.5, 1)
                VerticalLayoutGroup + ContentSizeFitter (vertical = PreferredSize)
```

`RectMask2D` rather than `Mask`: no extra material and no stencil buffer, which is the correct
tool for a rectangular clip.

### Modal windows

`modal: true` does three things: dims the screen with a full-screen `Image`, makes that image a
raycast target so clicks cannot reach the world behind it, and adds an input restraint through
`PlayerControl.LockPlayer` so the player cannot walk while the window is open. Closing the
window removes the restraint.

The restraint is keyed by a string unique to the window. See
[NeonNightSDK Core](NeonNightSDK-Core.md) for how restraints stack.

### EventSystem

uGUI buttons and input fields do nothing without an `EventSystem` in the scene. The game has
one during normal play, but relying on that produces UI that works everywhere except the one
scene that does not. `UiFactory.EnsureEventSystem()` creates one if it is missing.

## Architecture

| Class | Role |
|---|---|
| `HudKit` | Entry point. `Window(...)`, `Overlay(...)`, `CloseAll()` |
| `UiWindow` | Panel window; exposes the `Header` / `Sidebar` / `Body` / `Footer` builders |
| `UiBuilder` | Fluent widget API for one container |
| `UiOverlay` | Corner HUD with values bound to functions |
| `UiTheme` | All colors, font sizes and spacing |
| `UiFactory` | Internal plumbing: canvas, font, EventSystem, RectTransform helpers |

Sorting order: overlays at 900, windows from 1000 upward (each new window takes the next slot),
so a window always covers the HUD and the most recently opened window is on top.

### UiBuilder widgets

| Method | Produces |
|---|---|
| `Title` / `Heading` / `Text` / `Muted` | Text, wrapping, container grows to fit |
| `Button(label, onClick, width)` | Button sized to its label |
| `Link(label, onClick)` | Accent-colored text button, no background |
| `Input(placeholder, onSubmit)` | Single-line field; `onSubmit` fires on Enter |
| `Image(sprite, height, width)` | Image; the missing dimension follows the aspect ratio |
| `Spacer(height)` / `Separator()` | Gap / hairline rule |
| `Flexible()` | Absorbs remaining space |
| `Row(build)` / `Column(build)` | Nested containers |
| `Custom(name, build)` | Raw `RectTransform` escape hatch |
| `Clear()` | Removes every widget — the navigation primitive |

Every method returns the builder, so calls chain. When you need the underlying Unity component
(to keep a reference and update it later), pass `configure:`:

```csharp
header.Input("search...", Search, configure: f => _searchField = f);
```

## Getting started

### 1. Declare the dependency

```json
"Requires": { "neonnightsdk": "v0.2.0" }
```

### 2. Open a window

```csharp
using NeonNightSDK.Ui;

var win = HudKit.Window("Loja", 800, 500);
win.SetBody(b => b
    .Title("Bem-vindo")
    .Text("Escolha um item.")
    .Button("Comprar", Comprar));
```

That is a complete, centered, modal, closable window.

### 3. Add regions as needed

Regions are opt-in. `sidebarWidth: 0` (the default) means the window has no sidebar and
`win.Sidebar` is `null`. Ask for a size and the builder appears:

```csharp
var win = HudKit.Window("NeonNet", 1280, 800, sidebarWidth: 220, footerHeight: 30f);
```

## Examples

### An in-game browser

This is the complete structure for a browser with an address bar, bookmark sidebar, back
navigation, and swappable pages:

```csharp
using System.Collections.Generic;
using NeonNightSDK.Ui;
using UnityEngine.UI;

public sealed class Browser
{
    private UiWindow _win;
    private InputField _addressBar;
    private readonly Stack<string> _history = new Stack<string>();

    public void Open()
    {
        _win = HudKit.Window("NeonNet", 1280, 800, sidebarWidth: 220, footerHeight: 30f);

        _win.SetHeader(h => h
            .Button("<", Back, width: 44f)
            .Input("enter an address...", Navigate, configure: f => _addressBar = f)
            .Button("Ir", () => Navigate(_addressBar.text), width: 60f)
            .Flexible()
            .Button("X", _win.Close, width: 44f));

        _win.SetSidebar(s => s
            .Muted("FAVORITOS")
            .Separator()
            .Link("neonnet.home", () => Navigate("neonnet.home"))
            .Link("darkmarket.nn", () => Navigate("darkmarket.nn")));

        _win.SetFooter(f => f.Muted("conectado"));

        Navigate("neonnet.home");
    }

    private void Navigate(string url)
    {
        _history.Push(url);
        if (_addressBar != null) _addressBar.text = url;

        // SetBody clears the region, rebuilds it and scrolls back to the top.
        // One call per page: that is the whole navigation model.
        _win.SetBody(b => b
            .Title(url)
            .Muted("carregado agora")
            .Separator()
            .Text("Page content wraps automatically and the container grows with it.")
            .Spacer(12f)
            .Row(r => r
                .Button("Comprar", Comprar)
                .Button("Voltar", Back)
                .Flexible())
            .Separator()
            .Link("go to darkmarket.nn", () => Navigate("darkmarket.nn")));
    }

    private void Back()
    {
        if (_history.Count <= 1) return;
        _history.Pop();
        Navigate(_history.Pop());
    }
}
```

Add a page registry and it becomes a real site system:

```csharp
private readonly Dictionary<string, Action<UiBuilder>> _sites = new Dictionary<string, Action<UiBuilder>>();

public void Register(string url, Action<UiBuilder> page) => _sites[url] = page;

private void Navigate(string url)
{
    if (_sites.TryGetValue(url, out var page)) _win.SetBody(page);
    else _win.SetBody(b => b.Title("404").Muted($"'{url}' was not found."));
}
```

Other mods can then call `Register` to add their own sites — the extension point that turns
one mod's browser into a platform.

### Replacing NeedsHud

The 70-line hand-rolled HUD, with the same green-to-red color ramp:

```csharp
var hud = HudKit.Overlay("MeuMod_Needs", ScreenCorner.TopLeft);

foreach (var need in NeedsCatalog.All)
    hud.StatIcon(ctx.LoadSprite(need.IconPath),
                 () => zoey.GetStat(need.StatID).BaseValue / 100f);
```

You write no refresh loop. `StatIcon` takes a function returning `0..1`, and the overlay polls
it on its own interval (0.1s by default — imperceptible, and ~10x cheaper than the
every-frame refresh the original did).

Custom readout instead of a percentage:

```csharp
hud.StatIcon(icon, () => stat.BaseValue / 100f, format: p => $"{p * 100f:0}/100");
```

### Restyling

```csharp
var theme = UiTheme.Default.Clone();
theme.Accent = new Color(1f, 0.2f, 0.6f);
theme.WindowBackground = new Color(0.10f, 0.02f, 0.08f, 0.97f);

HudKit.Window("Loja", 800, 500, theme: theme);
```

## Limitations

- **Legacy `UnityEngine.UI.Text`, not TextMeshPro.** TMP ships with the game
  (`Unity.TextMeshPro.dll`) but is not referenced by the SDK. Legacy `Text` is what the
  existing mods proved working. Expect legacy-quality glyph rendering: no rich outlines, and
  small sizes are less crisp than TMP.
- **The built-in font only.** `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.
  Custom fonts require loading a `Font` yourself and assigning it via `configure:`.
- **`Window` is not draggable or resizable.** It is centered at a fixed size.
- **No nested scrolling.** Sidebar and body scroll independently; a scroll area inside a scroll
  area is not handled.
- **`Clear()` destroys via `Object.Destroy`**, which is deferred to end of frame. References to
  widgets from before a `Clear()` are dead — do not cache widget references across a
  `SetBody`.
- **`ScrollBodyToTop` is deferred one frame.** The layout has not been rebuilt at the moment
  widgets are created, so setting scroll position immediately would be overwritten.
- **Windows close on scene change** unless `persistAcrossScenes: true`. This is deliberate: a
  modal window surviving a transition would float over the loading screen and hold an input
  restraint on a player that no longer exists.
- **Overlays do not close on scene change** — they are `DontDestroyOnLoad` and hide themselves
  in the main menu. Call `Destroy()` in `OnModUnLoaded`.
- **No theme integration with the game.** The game has its own `ThemeManager` /
  `Theme` (see the modding wiki's Themes page); `UiTheme` is independent and does not follow it.
- **Not verified at runtime.** The kit compiles and the layout logic follows uGUI's documented
  behaviour, but it has not yet been exercised in a running game.

## Best practices

- Prefer `SetBody`/`SetSidebar` over `Body.Clear()` plus manual rebuilding — `SetBody` also
  resets the scroll position, which is almost always what you want after navigating.
- Use `Row` + `Flexible()` to align things, never manual positioning. A close button pinned
  right is `.Flexible().Button("X", win.Close)`.
- Keep references to input fields via `configure:`, not by walking the transform hierarchy.
- Give every window a stable `id` if you open it from more than one place; `HudKit.Window`
  closes and replaces a window that is already open under the same name.
- Call `HudKit.CloseAll()` and `overlay.Destroy()` in `OnModUnLoaded`.
- For anything the kit does not wrap, use `Custom(name, rect => ...)` rather than rebuilding a
  window by hand — the layout group still positions whatever you put in it.
- Do not put a `GraphicRaycaster` on a HUD overlay. `HudKit.Overlay` creates its canvas
  non-interactive precisely so it cannot swallow clicks meant for the game.

## References

- Code: `neonnightsdk/Ui/` (`HudKit`, `UiWindow`, `UiBuilder`, `UiOverlay`, `UiTheme`,
  `UiFactory`)
- Input restraints: `neonnightsdk/Core/PlayerControl.cs`
- Lifecycle and scheduling: [NeonNightSDK Core](NeonNightSDK-Core.md)
- World objects: [NeonNightSDK WorldKit](NeonNightSDK-WorldKit.md)
- Code this replaces: `testmod-master/Needs/NeedsHud.cs`, `testmod-master/Info/InfoWindow.cs`
- Unity types: `UnityEngine.UI.VerticalLayoutGroup`, `LayoutElement`, `ContentSizeFitter`,
  `ScrollRect`, `RectMask2D`, `InputField` (all in `UnityEngine.UI.dll`)

## Updates

- **v0.2.0** — First release of `HudKit`: `Window` with header/sidebar/body/footer regions,
  `Overlay` with value binding, `UiBuilder` fluent widgets, `UiTheme`. Added the
  `UnityEngine.InputLegacyModule` reference to the csproj (`UnityEngine.Input` lives in its own
  module; without it `Input.GetKeyDown` fails to compile with a misleading
  "the name 'Input' does not exist" error). Extracted `Core/PlayerControl` from the input
  restraint logic that was duplicated in `AnimationsKit` and TestMod's `SleepRobberyService`.
