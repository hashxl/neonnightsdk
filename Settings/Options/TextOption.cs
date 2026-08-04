using System;
using NeonNightSDK.Ui;
using UnityEngine.UI;

namespace NeonNightSDK.Settings.Options
{
    // Free text, drawn as an input field.
    public sealed class TextOption : ValueOption<string>
    {
        private readonly string _placeholder;

        public TextOption(string key, string label, string description, string placeholder,
            Func<string> get, Action<string> set)
            : base(key, label, description, get, set)
        {
            _placeholder = placeholder ?? string.Empty;
        }

        public override void Render(UiBuilder body)
        {
            body.ControlRow(Label, Description, slot =>
                slot.Input(_placeholder, Commit, configure: field =>
                {
                    field.text = Value ?? string.Empty;
                    // onSubmit alone (what UiBuilder.Input wires) only fires on Enter, so a
                    // player who types and then clicks another option would lose the edit.
                    // onEndEdit also fires when the field loses focus.
                    field.onEndEdit.AddListener(Commit);
                }));
        }

        private void Commit(string value)
        {
            if (Value == value) return;

            Value = value;
            NotifyChanged();
        }

        protected override string Format(string value) => value ?? string.Empty;

        protected override bool TryParse(string raw, out string value)
        {
            value = raw;
            return true;
        }
    }
}
