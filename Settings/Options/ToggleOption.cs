using System;
using NeonNightSDK.Ui;

namespace NeonNightSDK.Settings.Options
{
    // A boolean, drawn as a checkbox.
    public sealed class ToggleOption : ValueOption<bool>
    {
        public ToggleOption(string key, string label, string description, Func<bool> get, Action<bool> set)
            : base(key, label, description, get, set)
        {
        }

        public override void Render(UiBuilder body)
        {
            body.Toggle(Label, Value, value =>
            {
                Value = value;
                NotifyChanged();
            }, Description);
        }

        // Lower-case literals rather than bool.ToString() ("True"/"False"), so the file reads
        // like JSON and a hand-edited "true" parses. bool.TryParse accepts both anyway.
        protected override string Format(bool value) => value ? "true" : "false";

        protected override bool TryParse(string raw, out bool value) => bool.TryParse(raw, out value);
    }
}
