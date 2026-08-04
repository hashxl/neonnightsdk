using System;
using System.Collections.Generic;
using NeonNightSDK.Core;
using NeonNightSDK.Settings.Options;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonNightSDK.Settings
{
    // In-game settings for mods — the entry point.
    //
    //   ctx.Settings("Needs & Sleep")
    //      .Toggle("hunger", "Enable hunger", () => Cfg.Hunger, v => Cfg.Hunger = v);
    //
    // A mod declares WHAT it can be configured with; the kit does the rest: renders the
    // controls, saves them to disk, loads them back on the next launch, and puts a MOD
    // SETTINGS entry in the game's pause menu.
    //
    // The mod never writes UI code, never touches a file, and never handles input.
    public static class SettingsKit
    {
        private static readonly List<ModSettingsPage> PageList = new List<ModSettingsPage>();
        private static bool _installed;

        // Every registered page, SDK first and then alphabetically — the order the sidebar
        // shows them in.
        public static IReadOnlyList<ModSettingsPage> Pages => PageList;

        // ---- registration ---------------------------------------------------------------

        // Registers (or extends) a mod's settings page and immediately applies whatever was
        // saved for it. Because the load happens before this returns, a mod can read its own
        // configuration on the very next line of OnModLoaded.
        //
        // Prefer ModContext.Settings(...) — it fills the id in from the manifest.
        public static ModSettingsPage Register(string id, string title, Action<ModSettingsPage> build,
            string description = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                SdkLog.Error("SettingsKit.Register: an id is required (it names the settings file).");
                return null;
            }

            Install();

            var page = Find(id);
            if (page == null)
            {
                page = new ModSettingsPage(id, title, description);
                PageList.Add(page);
                Sort();
            }
            else
            {
                // Registering twice is legitimate — a mod may add a section from a service that
                // initialises later. Only the description is refreshed; options are appended,
                // and ModSettingsPage.Add rejects duplicate keys.
                if (!string.IsNullOrEmpty(description)) page.Description = description;
            }

            SdkLog.SafeInvoke($"SettingsKit: building page '{id}'", () => build?.Invoke(page));
            page.Load();

            return page;
        }

        public static ModSettingsPage Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (var page in PageList)
                if (string.Equals(page.Id, id, StringComparison.OrdinalIgnoreCase))
                    return page;

            return null;
        }

        // Drops a page — for a mod that unloads at runtime. Its file is left alone, so the
        // settings come back if the mod returns.
        public static bool Unregister(string id)
        {
            var page = Find(id);
            if (page == null) return false;

            PageList.Remove(page);
            if (ModSettingsWindow.IsOpen) ModSettingsWindow.Close();
            return true;
        }

        // ---- window ---------------------------------------------------------------------

        public static void Open(string pageId = null) => ModSettingsWindow.Open(pageId);

        public static void Close() => ModSettingsWindow.Close();

        public static void Toggle(string pageId = null) => ModSettingsWindow.Toggle(pageId);

        public static bool IsOpen => ModSettingsWindow.IsOpen;

        // ---- installation ---------------------------------------------------------------

        // Idempotent, and called from Register — a mod never needs it.
        public static void Install()
        {
            if (_installed) return;
            // Set BEFORE registering the SDK's own page: RegisterOwnPage goes through
            // Register(), which calls back into Install().
            _installed = true;

            SdkRuntime.Install();
            RegisterOwnPage();
            PauseMenuButtonInjector.Install();
            SdkEvents.OnUpdate += PollHotkey;

            SdkLog.Info("SettingsKit installed (pause-menu entry + hotkey).");
        }

        internal static void Shutdown()
        {
            if (!_installed) return;
            _installed = false;

            SdkEvents.OnUpdate -= PollHotkey;
            PauseMenuButtonInjector.Uninstall();
            ModSettingsWindow.Close();
            PageList.Clear();
        }

        private static void RegisterOwnPage()
        {
            Register(SdkSettings.PageId, "NeonNight SDK", page => page
                    .Keybind("hotkey", "Settings hotkey",
                        () => SdkSettings.SettingsHotkey,
                        v => SdkSettings.SettingsHotkey = v,
                        "Opens this window from anywhere, including outside the pause menu.")
                    .Toggle("pauseMenuButton", "Show button in the pause menu",
                        () => SdkSettings.ShowPauseMenuButton,
                        v => SdkSettings.ShowPauseMenuButton = v,
                        "Takes effect the next time the pause menu is opened."),
                description: "Settings for the mod framework itself.");
        }

        private static void PollHotkey()
        {
            if (SdkSettings.SettingsHotkey == KeyCode.None) return;
            if (!Input.GetKeyDown(SdkSettings.SettingsHotkey)) return;

            // The rebind control is waiting for this very keypress — it must not also be read
            // as "open/close the window".
            if (KeybindOption.IsCapturing) return;
            if (IsTypingInAField()) return;

            ModSettingsWindow.Toggle();
        }

        // A hotkey that fires while the player is typing turns every matching letter into a
        // window toggle — the classic bug in mod hotkeys.
        private static bool IsTypingInAField()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            return selected != null && selected.GetComponentInChildren<InputField>() != null;
        }

        // The SDK's own page is pinned first (it holds the way back in if the button breaks);
        // the rest are alphabetical, since no mod has a claim to being higher than another.
        private static void Sort()
        {
            PageList.Sort((a, b) =>
            {
                if (a.Id == SdkSettings.PageId) return b.Id == SdkSettings.PageId ? 0 : -1;
                if (b.Id == SdkSettings.PageId) return 1;
                return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
