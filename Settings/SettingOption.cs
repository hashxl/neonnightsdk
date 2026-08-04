using System;
using NeonNightSDK.Core;
using NeonNightSDK.Ui;

namespace NeonNightSDK.Settings
{
    // Shared plumbing for every option: identity, the description line, and the "something
    // changed, persist it" notification. Concrete options only implement Render().
    public abstract class SettingOption : ISettingOption
    {
        protected SettingOption(string key, string label, string description)
        {
            Key = key;
            Label = label ?? key ?? string.Empty;
            Description = description;
        }

        public string Key { get; }
        public string Label { get; }

        // Optional muted line under the control. This is where a mod explains what an option
        // actually does — the single biggest difference between a usable settings page and a
        // list of cryptic labels.
        public string Description { get; }

        // Set by ModSettingsPage.Add. Lets an option report a change without knowing anything
        // about the store or the window.
        internal ModSettingsPage Page { get; set; }

        public abstract void Render(UiBuilder body);

        public virtual string Serialize() => null;
        public virtual void Deserialize(string raw) { }
        public virtual void ResetToDefault() { }

        protected void NotifyChanged() => Page?.OnOptionChanged(this);
    }

    // An option backed by a value that lives in the MOD's own fields, reached through a
    // getter/setter pair.
    //
    // Why delegates instead of the kit owning the value: the mod's code reads `Config.Fome`
    // everywhere, in hot paths, and should not have to go through a dictionary lookup or
    // remember to sync anything. The option writes straight into the field it was given, so
    // loading from disk and moving the slider both end at the same assignment.
    public abstract class ValueOption<T> : SettingOption
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private readonly T _default;

        protected ValueOption(string key, string label, string description,
            Func<T> get, Action<T> set)
            : base(key, label, description)
        {
            _get = get;
            _set = set;

            // The default is whatever the mod's field held at registration time — i.e. the
            // literal it was initialised with in code. Registration always happens before the
            // saved file is applied, so this captures the shipped default and not the player's
            // last session. That's why the mod never has to pass a defaultValue.
            _default = Read();
        }

        public T Value
        {
            get => Read();
            set => Write(value);
        }

        // How the value is written to (and read back from) the settings file. Always
        // culture-invariant in the implementations — a machine with a pt-BR locale writes
        // "0,5" by default, which then fails to parse on an en-US machine (and vice versa).
        protected abstract string Format(T value);
        protected abstract bool TryParse(string raw, out T value);

        public override string Serialize() => Format(Read());

        public override void Deserialize(string raw)
        {
            if (raw == null) return;

            if (!TryParse(raw, out var value))
            {
                SdkLog.Warn($"Settings: '{Key}' could not read the saved value '{raw}' " +
                            $"({typeof(T).Name} expected) — keeping the default.");
                return;
            }

            Write(value);
        }

        public override void ResetToDefault() => Write(_default);

        // Both sides are guarded: a mod's getter or setter is arbitrary code (it may touch a
        // destroyed object, or throw on a value it doesn't like), and one bad option must not
        // abort the load of every other option in the file.
        protected T Read()
        {
            if (_get == null) return default;

            try
            {
                return _get();
            }
            catch (Exception ex)
            {
                SdkLog.Error($"Settings: getter for '{Key}' threw, using default: {ex.Message}");
                return default;
            }
        }

        protected void Write(T value)
        {
            if (_set == null) return;

            try
            {
                _set(value);
            }
            catch (Exception ex)
            {
                SdkLog.Error($"Settings: setter for '{Key}' threw, value not applied: {ex.Message}");
            }
        }
    }
}
