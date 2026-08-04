using UnityEngine;

namespace NeonNightSDK.Settings
{
    // The SDK's own settings, shown as the first page in the window.
    //
    // It exists for a practical reason as much as a demonstrative one: if the pause-menu
    // button ever fails to appear (a game patch moves the menu around, another mod replaces
    // it), the hotkey is the way back in — and it has to be reachable and rebindable without
    // editing a file by hand.
    public static class SdkSettings
    {
        internal const string PageId = "neonnightsdk";

        // Opens/closes the settings window from anywhere. KeyCode.None disables it.
        public static KeyCode SettingsHotkey = KeyCode.F10;

        // Lets a player who dislikes the extra entry drop it and keep the hotkey.
        public static bool ShowPauseMenuButton = true;
    }
}
