using System;
using ANToolkit.UI;
using NeonNightSDK.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NeonNightSDK.Settings
{
    // Adds a MOD SETTINGS entry to the game's pause menu.
    //
    // HOW IT HOOKS IN: ANToolkit.UI.PauseMenu exposes `public static UnityEvent OnOpened`,
    // fired from its Awake(). That's a supported extension point in the game's own code, so no
    // Harmony patch and no IL rewriting are involved — the menu is a prefab instantiated fresh
    // on every pause, and we add to that instance.
    //
    // HOW IT LOOKS RIGHT: the entry is a CLONE of an existing menu button, not a new one. The
    // game's buttons carry an ANButton, a MainMenuButton (the hover animation driver), an
    // Animator with the menu's state machine and a sliced sprite — reproducing that by hand
    // would drift from the game's look on any art change. Cloning inherits all of it, and only
    // the label and the click handler are replaced.
    internal static class PauseMenuButtonInjector
    {
        private const string ButtonName = "NN_ModSettingsButton";
        private const string ButtonLabel = "MOD SETTINGS";

        // The entry goes right after the first of these that exists, which puts it with the
        // other configuration entries and above the "leave the game" ones. Matched on the
        // button's own text, not on a child index — an index would silently point at a
        // different button after any menu edit.
        private static readonly string[] PreferredAnchors = { "OPTIONS", "DIALOGUE LOG", "LOAD GAME" };

        private static UnityAction _handler;

        internal static void Install()
        {
            if (_handler != null) return;

            _handler = OnPauseOpened;
            PauseMenu.OnOpened.AddListener(_handler);
        }

        internal static void Uninstall()
        {
            if (_handler == null) return;

            PauseMenu.OnOpened.RemoveListener(_handler);
            _handler = null;
        }

        private static void OnPauseOpened()
        {
            // Deferred by one frame: OnOpened is invoked from PauseMenu.Awake(), so the menu's
            // own components have had their Awake but not their Start, and cloning mid-Awake
            // means the clone's Start runs against a half-built parent. One frame at
            // timeScale 0 is invisible to the player (the Scheduler ticks from Update, which
            // keeps running while paused).
            Scheduler.NextFrame(Inject).Named("SettingsKit.InjectPauseButton");
        }

        private static void Inject()
        {
            if (!SdkSettings.ShowPauseMenuButton) return;

            if (SettingsKit.Pages.Count == 0)
            {
                SdkLog.Info("SettingsKit: no mod registered any settings, pause-menu entry skipped.");
                return;
            }

            var menu = UnityEngine.Object.FindObjectOfType<PauseMenu>();
            if (menu == null) return;

            // A second PauseMenu instance is created on every pause, so this normally can't
            // happen — but a mod re-invoking OnOpened, or two SDK copies loaded at once, would
            // otherwise stack duplicate entries.
            if (FindExisting(menu) != null) return;

            var model = FindModelButton(menu);
            if (model == null)
            {
                SdkLog.Warn("SettingsKit: could not find a button to clone in the pause menu — " +
                            $"use the hotkey ({SdkSettings.SettingsHotkey}) instead.");
                return;
            }

            SdkLog.SafeInvoke("SettingsKit: injecting the pause-menu entry", () => Clone(model));
        }

        private static void Clone(ANButton model)
        {
            var clone = UnityEngine.Object.Instantiate(model.gameObject, model.transform.parent);
            clone.name = ButtonName;
            // Directly below the button it was cloned from, so it lands inside the existing
            // list instead of at the end of it.
            clone.transform.SetSiblingIndex(model.transform.GetSiblingIndex() + 1);

            var button = clone.GetComponent<ANButton>();
            if (button == null)
            {
                SdkLog.Error("SettingsKit: the cloned pause-menu button has no ANButton — aborting.");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            ClearInputBind(button);
            SilenceClonedHandlers(button);

            button.SetText(ButtonLabel);
            button.OnRelease.AddListener(_ => SettingsKit.Open());

            SdkLog.Info($"SettingsKit: pause-menu entry added (cloned from '{LabelOf(model)}').");
        }

        // The clone inherited the model's keyboard/controller bind — RESUME is bound to Cancel,
        // for instance — and ANButton registers it in OnEnable, which already ran inside
        // Instantiate. Clearing the field alone would leave the registration behind, so the
        // component is disabled FIRST (OnDisable unregisters using the bind it still has), and
        // only then emptied.
        private static void ClearInputBind(ANButton button)
        {
            if (string.IsNullOrEmpty(button.Bind)) return;

            button.gameObject.SetActive(false);
            button.Bind = string.Empty;
            button.gameObject.SetActive(true);
        }

        // The clone also inherited the model's click actions — an untouched copy of OPTIONS
        // opens the options menu. Runtime listeners come off with RemoveAllListeners, but the
        // ones wired in the Unity inspector are PERSISTENT and survive it; those can only be
        // switched off individually.
        private static void SilenceClonedHandlers(ANButton button)
        {
            Silence(button.OnClick);
            Silence(button.OnRelease);
            Silence(button.OnHeld);
            Silence(button.OnHold);
            Silence(button.OnSelected);
        }

        private static void Silence(UnityEventBase unityEvent)
        {
            if (unityEvent == null) return;

            for (var i = 0; i < unityEvent.GetPersistentEventCount(); i++)
                unityEvent.SetPersistentListenerState(i, UnityEventCallState.Off);

            unityEvent.RemoveAllListeners();
        }

        private static Transform FindExisting(PauseMenu menu)
        {
            foreach (var child in menu.GetComponentsInChildren<Transform>(includeInactive: true))
                if (child.name == ButtonName)
                    return child;

            return null;
        }

        private static ANButton FindModelButton(PauseMenu menu)
        {
            var buttons = menu.GetComponentsInChildren<ANButton>(includeInactive: true);
            if (buttons == null || buttons.Length == 0) return null;

            foreach (var anchor in PreferredAnchors)
                foreach (var button in buttons)
                    if (string.Equals(LabelOf(button), anchor, StringComparison.OrdinalIgnoreCase))
                        return button;

            // Fallback for a menu whose entries were renamed or translated: the first button
            // carrying MainMenuButton is still, by construction, a styled menu entry.
            foreach (var button in buttons)
                if (button.GetComponent<MainMenuButton>() != null)
                    return button;

            return buttons[0];
        }

        private static string LabelOf(ANButton button)
        {
            var text = button == null ? null : button.GetComponentInChildren<Text>();
            return text == null ? null : text.text?.Trim();
        }
    }
}
