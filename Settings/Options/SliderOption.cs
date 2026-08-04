using System;
using System.Globalization;
using NeonNightSDK.Ui;
using UnityEngine;

namespace NeonNightSDK.Settings.Options
{
    // A number inside a range, drawn as a slider with a live readout.
    public sealed class SliderOption : ValueOption<float>
    {
        private readonly float _min;
        private readonly float _max;
        private readonly bool _wholeNumbers;

        public SliderOption(string key, string label, string description,
            float min, float max, bool wholeNumbers, Func<float> get, Action<float> set)
            : base(key, label, description, get, set)
        {
            _min = min;
            _max = max;
            _wholeNumbers = wholeNumbers;
        }

        public override void Render(UiBuilder body)
        {
            body.Slider(Label, _min, _max, Value, value =>
            {
                Value = value;
                NotifyChanged();
            }, _wholeNumbers, Description);
        }

        // "R" (round-trip) instead of a fixed number of decimals: a slider bound to a float
        // that happens to hold 0.3333 must come back as 0.3333, not 0.3.
        protected override string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        protected override bool TryParse(string raw, out float value)
        {
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            // Clamped here rather than trusted: the range can shrink between mod versions, and
            // a value outside it would leave the slider handle pinned at an end while the mod
            // ran on a number the player can no longer reproduce.
            value = Mathf.Clamp(value, _min, _max);
            if (_wholeNumbers) value = Mathf.Round(value);
            return true;
        }
    }
}
