using System.Collections.Generic;
using NeonNightSDK.Core;
using UnityEngine;

namespace NeonNightSDK.Ui
{
    // Entry point for everything UI.
    //
    // Two shapes cover what mods actually need:
    //
    //   HudKit.Window(...)   a panel with header / sidebar / body / footer — menus, shops,
    //                        journals, an in-game browser.
    //   HudKit.Overlay(...)  a corner HUD bound to live values — needs bars, timers, counters.
    //
    // Both replace the hand-rolled Canvas + CanvasScaler + Image/Text/RectTransform blocks
    // that every mod was writing from scratch (see TestMod's NeedsHud and InfoWindow).
    public static class HudKit
    {
        // Overlays sit under windows so a window always covers the HUD. Each new window takes
        // the next slot up, so the most recently opened one is on top.
        private const int OverlaySortingOrder = 900;
        private const int WindowSortingOrderBase = 1000;

        private static readonly List<UiWindow> OpenWindows = new List<UiWindow>();

        public static IReadOnlyList<UiWindow> Windows => OpenWindows;

        // Creates and shows a window.
        //
        //   var win = HudKit.Window("NeonNet", 1280, 800, sidebarWidth: 220);
        //
        // sidebarWidth / footerHeight default to 0, meaning "no such region" — ask for them
        // and the corresponding builder (win.Sidebar / win.Footer) becomes available.
        //
        // modal (default true) dims the screen, blocks clicks reaching the world, and holds an
        // input restraint so the player can't walk around while it's open. Pass false for a
        // non-blocking panel.
        //
        // useGameCancelStack routes Escape through the game's own NNUICancelStack instead of
        // polling the key, and blocks MenuManager's pause toggle while the window is up. Turn
        // it on for any window that can be open ON TOP of the game's UI (the pause menu, a
        // shop) — otherwise one Escape press is seen by both and closes both. It replaces
        // closeOnEscape rather than adding to it.
        public static UiWindow Window(
            string title,
            float width,
            float height,
            float sidebarWidth = 0f,
            float footerHeight = 0f,
            float? headerHeight = null,
            bool modal = true,
            bool closeButton = true,
            bool closeOnEscape = true,
            bool persistAcrossScenes = false,
            bool useGameCancelStack = false,
            UiTheme theme = null,
            string id = null)
        {
            SdkRuntime.Install();

            var resolvedTheme = theme ?? UiTheme.Default;
            var name = string.IsNullOrEmpty(id) ? $"NeonNightSDK_Window_{title}" : id;

            // Reopening an already-open window would stack two identical canvases, and only
            // the top one would be reachable. Close the old one first.
            var existing = OpenWindows.Find(w => w.IsOpen && w.Name == name);
            if (existing != null)
            {
                SdkLog.Info($"Ui: window '{name}' was already open, replacing it.");
                existing.Close();
            }

            OpenWindows.RemoveAll(w => !w.IsOpen);

            var window = new UiWindow(
                name,
                title,
                width,
                height,
                resolvedTheme,
                WindowSortingOrderBase + OpenWindows.Count * 10,
                sidebarWidth,
                headerHeight ?? resolvedTheme.HeaderHeight,
                footerHeight,
                modal,
                closeButton,
                closeOnEscape,
                persistAcrossScenes,
                useGameCancelStack);

            OpenWindows.Add(window);
            window.Closed += () => OpenWindows.Remove(window);

            return window;
        }

        // Creates a corner HUD.
        //
        //   var hud = HudKit.Overlay("MeuMod_Needs");
        //   hud.StatIcon(iconeFome, () => zoey.GetStat("hunger").BaseValue / 100f);
        //
        // refreshInterval is how often the bound values are polled. 0.1s is imperceptible to
        // the player and ~10x cheaper than the every-frame refresh the hand-rolled HUD did.
        public static UiOverlay Overlay(
            string id,
            ScreenCorner corner = ScreenCorner.TopLeft,
            UiTheme theme = null,
            Vector2? margin = null,
            float refreshInterval = 0.1f,
            bool hideInMainMenu = true)
        {
            SdkRuntime.Install();

            return new UiOverlay(
                string.IsNullOrEmpty(id) ? "NeonNightSDK_Overlay" : id,
                corner,
                theme ?? UiTheme.Default,
                OverlaySortingOrder,
                margin ?? new Vector2(24f, 24f),
                refreshInterval,
                hideInMainMenu);
        }

        // Closes every window this kit opened. Handy in OnModUnLoaded, or as a panic button.
        public static void CloseAll()
        {
            // ToArray: Close() removes from OpenWindows through the Closed callback.
            foreach (var window in OpenWindows.ToArray())
                window.Close();

            OpenWindows.Clear();
        }
    }
}
