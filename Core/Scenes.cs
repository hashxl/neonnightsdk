using System;
using UnityEngine.SceneManagement;

namespace NeonNightSDK.Core
{
    // The `if (scene.name == "MainMenu") return;` guard was copy-pasted into practically
    // every mod service (TestMod.OnSceneLoaded, NeedsService.OnSceneLoaded, ...).
    // Centralized here — and SdkEvents.OnGameplaySceneReady already applies this filter for
    // you, so most of the time you don't need to write the if at all.
    public static class Scenes
    {
        public const string MainMenu = "MainMenu";

        public static string Active => SceneManager.GetActiveScene().name;

        public static bool IsMainMenu(string sceneName) =>
            string.Equals(sceneName, MainMenu, StringComparison.OrdinalIgnoreCase);

        // "An actual gameplay scene": anything that isn't the main menu.
        public static bool IsGameplay(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && !IsMainMenu(sceneName);
    }
}
