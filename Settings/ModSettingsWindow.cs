using NeonNightSDK.Core;
using NeonNightSDK.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace NeonNightSDK.Settings
{
    // The window itself: mods down the left, the selected mod's options on the right — the
    // shape every settings menu of this kind uses, because it stays readable whether one mod
    // is installed or twenty.
    //
    // It owns no state beyond "which page is selected": every value on screen is read from the
    // mod's own fields through the options at render time, so a setting changed by code while
    // the window is open shows up the next time the page is drawn.
    internal static class ModSettingsWindow
    {
        private const string WindowId = "NeonNightSDK_ModSettings";

        private static UiWindow _window;
        private static ModSettingsPage _current;

        internal static bool IsOpen => _window != null && _window.IsOpen;

        internal static void Toggle(string pageId = null)
        {
            if (IsOpen) Close();
            else Open(pageId);
        }

        internal static void Open(string pageId = null)
        {
            if (SettingsKit.Pages.Count == 0)
            {
                SdkLog.Warn("Settings: nothing to show — no mod has registered a settings page.");
                return;
            }

            // Already up (the player clicked MOD SETTINGS twice, or a mod called Open while it
            // was open): just navigate. Falling through would let HudKit close the existing
            // window by id — and that closure runs OnWindowClosed, wiping the selection we'd
            // just made.
            if (IsOpen)
            {
                var requested = SettingsKit.Find(pageId);
                if (requested != null) Select(requested);
                return;
            }

            // useGameCancelStack: this window opens ON TOP of the pause menu, so Escape has to
            // close it and nothing else. See UiWindow.BindGameCancel.
            _window = HudKit.Window(
                "MOD SETTINGS",
                width: 1150f,
                height: 720f,
                sidebarWidth: 300f,
                footerHeight: 52f,
                useGameCancelStack: true,
                id: WindowId);

            _window.Closed += OnWindowClosed;
            // Selected only after the window exists, for the same reason.
            _current = SettingsKit.Find(pageId) ?? SettingsKit.Pages[0];

            BuildSidebar();
            BuildFooter();
            ShowCurrentPage();
        }

        internal static void Close() => _window?.Close();

        private static void OnWindowClosed()
        {
            _window = null;
            _current = null;
        }

        private static void Select(ModSettingsPage page)
        {
            if (ReferenceEquals(page, _current)) return;

            _current = page;
            // The sidebar is rebuilt too, and not just the body: the highlight lives on the
            // buttons themselves.
            BuildSidebar();
            ShowCurrentPage();
        }

        private static void BuildSidebar()
        {
            _window?.SetSidebar(side =>
            {
                side.Muted($"{SettingsKit.Pages.Count} mod(s) with settings");
                side.Spacer(4f);

                foreach (var page in SettingsKit.Pages)
                {
                    // Captured per iteration: without the local, every button would close over
                    // the same loop variable and open the last page.
                    var target = page;
                    var isSelected = ReferenceEquals(page, _current);

                    side.Button(page.Title, () => Select(target), configure: button =>
                    {
                        if (!isSelected) return;

                        // Blended rather than the raw accent: solid cyan behind light button
                        // text is unreadable.
                        var image = button.targetGraphic as Image;
                        if (image != null)
                            image.color = Color.Lerp(_window.Theme.ButtonBackground, _window.Theme.Accent, 0.35f);
                    });
                }
            });
        }

        private static void ShowCurrentPage()
        {
            _window?.SetBody(body => _current?.Render(body));
        }

        private static void BuildFooter()
        {
            _window?.SetFooter(footer =>
            {
                footer.Button("Restore defaults", RestoreDefaults, width: 190f);
                footer.Flexible();
                // Players ask where settings live every single time; showing the folder costs
                // nothing and saves the question.
                footer.Muted($"Saved automatically to {SettingsStore.DirectoryPath}");
            });
        }

        private static void RestoreDefaults()
        {
            if (_current == null) return;

            _current.ResetToDefaults();
            // Re-rendered from scratch so every control picks up its reset value — the widgets
            // hold their own copy of it once created.
            ShowCurrentPage();
            SdkLog.Info($"Settings: '{_current.Id}' restored to defaults.");
        }
    }
}
