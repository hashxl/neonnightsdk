using System;
using System.Collections.Generic;
using NeonNightSDK.Core;
using NeonNightSDK.Settings.Options;
using NeonNightSDK.Ui;
using UnityEngine;

namespace NeonNightSDK.Settings
{
    // One mod's settings page: an ordered list of options plus the file they persist to.
    //
    // Built fluently, once, in OnModLoaded:
    //
    //   ctx.Settings("Needs & Sleep")
    //      .Section("Hunger")
    //      .Toggle("hunger.enabled", "Enable hunger", () => Cfg.Hunger, v => Cfg.Hunger = v,
    //              "Zoey gets hungry as time passes.")
    //      .Slider("hunger.rate", "Decay rate", 0f, 5f, () => Cfg.Rate, v => Cfg.Rate = v)
    //      .Button("Reset", "Reset now", () => Needs.ResetAll());
    //
    // Every builder method takes a KEY as well as a label. The key is what ends up in the
    // JSON file, so it must stay stable across versions — labels are free to change (and to be
    // translated) without a player losing their settings.
    public sealed class ModSettingsPage
    {
        private readonly List<ISettingOption> _options = new List<ISettingOption>();

        internal ModSettingsPage(string id, string title, string description)
        {
            Id = id;
            Title = string.IsNullOrEmpty(title) ? id : title;
            Description = description;
        }

        // Stable identity — the mod's UniqueIdentifier. Also the settings file's name.
        public string Id { get; }

        // What the sidebar shows.
        public string Title { get; }

        // Optional blurb under the page title.
        public string Description { get; internal set; }

        public IReadOnlyList<ISettingOption> Options => _options;

        // Fires after any option on this page changes, with the option that changed. Use it to
        // apply a setting live instead of only on the next load. The argument is null when the
        // whole page was reset to defaults.
        public event Action<ISettingOption> Changed;

        // ---- building ---------------------------------------------------------------

        public ModSettingsPage Toggle(string key, string label, Func<bool> get, Action<bool> set,
            string description = null) =>
            Add(new ToggleOption(key, label, description, get, set));

        public ModSettingsPage Slider(string key, string label, float min, float max,
            Func<float> get, Action<float> set, bool wholeNumbers = false, string description = null) =>
            Add(new SliderOption(key, label, description, min, max, wholeNumbers, get, set));

        public ModSettingsPage Select(string key, string label, string[] choices,
            Func<int> get, Action<int> set, string description = null) =>
            Add(new SelectOption(key, label, description, choices, get, set));

        public ModSettingsPage Text(string key, string label, Func<string> get, Action<string> set,
            string placeholder = null, string description = null) =>
            Add(new TextOption(key, label, description, placeholder, get, set));

        public ModSettingsPage Keybind(string key, string label, Func<KeyCode> get, Action<KeyCode> set,
            string description = null) =>
            Add(new KeybindOption(key, label, description, get, set));

        public ModSettingsPage Button(string label, string buttonLabel, Action action,
            string description = null) =>
            Add(new ActionOption(label, description, buttonLabel, action));

        public ModSettingsPage Section(string label, string description = null) =>
            Add(new SectionOption(label, description));

        // Adds any option, including one a mod wrote itself by implementing ISettingOption.
        // That's the extension point: a mod needing a control this kit doesn't ship writes the
        // strategy and drops it in here, with persistence and rendering working the same way.
        public ModSettingsPage Add(ISettingOption option)
        {
            if (option == null) return this;

            if (!string.IsNullOrEmpty(option.Key) && FindByKey(option.Key) != null)
            {
                SdkLog.Warn($"Settings: page '{Id}' already has an option keyed '{option.Key}' — " +
                            "the duplicate was ignored (two options sharing a key would " +
                            "overwrite each other in the file).");
                return this;
            }

            if (option is SettingOption owned) owned.Page = this;
            _options.Add(option);
            return this;
        }

        // ---- rendering --------------------------------------------------------------

        internal void Render(UiBuilder body)
        {
            body.Title(Title);

            if (!string.IsNullOrEmpty(Description))
                body.Muted(Description);

            body.Spacer();

            foreach (var option in _options)
                SdkLog.SafeInvoke($"Settings: rendering '{Id}/{option.Key ?? option.Label}'",
                    () => option.Render(body));
        }

        // ---- persistence -------------------------------------------------------------

        internal void OnOptionChanged(ISettingOption option)
        {
            Save();

            var changed = Changed;
            if (changed != null)
                SdkLog.SafeInvoke($"Settings: '{Id}' Changed handler", () => changed(option));
        }

        // Applies the values on disk to the mod's fields. Called once right after registration,
        // so a mod's OnModLoaded can read its own config immediately afterwards.
        internal void Load()
        {
            var values = SettingsStore.Load(Id);
            if (values.Count == 0) return;

            foreach (var option in _options)
            {
                if (string.IsNullOrEmpty(option.Key)) continue;
                if (!values.TryGetValue(option.Key, out var raw)) continue;

                SdkLog.SafeInvoke($"Settings: loading '{Id}/{option.Key}'", () => option.Deserialize(raw));
            }

            SdkLog.Info($"Settings: '{Id}' loaded ({values.Count} value(s)).");
        }

        internal void Save()
        {
            var values = new Dictionary<string, string>();

            foreach (var option in _options)
            {
                if (string.IsNullOrEmpty(option.Key)) continue;

                var serialized = option.Serialize();
                if (serialized != null) values[option.Key] = serialized;
            }

            SettingsStore.Save(Id, values);
        }

        internal void ResetToDefaults()
        {
            foreach (var option in _options)
                SdkLog.SafeInvoke($"Settings: resetting '{Id}/{option.Key ?? option.Label}'",
                    option.ResetToDefault);

            Save();

            var changed = Changed;
            if (changed != null)
                SdkLog.SafeInvoke($"Settings: '{Id}' Changed handler", () => changed(null));
        }

        private ISettingOption FindByKey(string key)
        {
            foreach (var option in _options)
                if (option.Key == key)
                    return option;

            return null;
        }
    }
}
