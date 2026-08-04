using System;
using System.Globalization;
using NeonNightSDK.Ui;
using UnityEngine;

namespace NeonNightSDK.Settings.Options
{
    // A choice from a fixed list, drawn as a cycler. The mod's field holds the INDEX.
    public sealed class SelectOption : ValueOption<int>
    {
        private readonly string[] _choices;

        public SelectOption(string key, string label, string description,
            string[] choices, Func<int> get, Action<int> set)
            : base(key, label, description, get, set)
        {
            _choices = choices ?? new string[0];
        }

        public override void Render(UiBuilder body)
        {
            body.Select(Label, _choices, Mathf.Clamp(Value, 0, _choices.Length - 1), index =>
            {
                Value = index;
                NotifyChanged();
            }, Description);
        }

        // Persisted as the choice TEXT, not the index. Mods reorder and insert choices between
        // versions; an index silently becomes a different setting when that happens, while a
        // name either still matches or is detectably gone.
        protected override string Format(int value) =>
            value >= 0 && value < _choices.Length
                ? _choices[value]
                : value.ToString(CultureInfo.InvariantCulture);

        protected override bool TryParse(string raw, out int value)
        {
            for (var i = 0; i < _choices.Length; i++)
            {
                if (string.Equals(_choices[i], raw, StringComparison.OrdinalIgnoreCase))
                {
                    value = i;
                    return true;
                }
            }

            // Accepts a bare index too, so a file written before this option had names (or
            // edited by hand) still loads.
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && value >= 0 && value < _choices.Length)
                return true;

            value = 0;
            return false;
        }
    }
}
