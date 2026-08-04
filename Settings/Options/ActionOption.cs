using System;
using NeonNightSDK.Core;
using NeonNightSDK.Ui;

namespace NeonNightSDK.Settings.Options
{
    // A button that runs something right now — "reset progress", "respawn the vendor",
    // "dump state to the log". Holds no value, so it has no Key and is never persisted.
    public sealed class ActionOption : SettingOption
    {
        private readonly Action _action;
        private readonly string _buttonLabel;

        public ActionOption(string label, string description, string buttonLabel, Action action)
            : base(null, label, description)
        {
            _buttonLabel = string.IsNullOrEmpty(buttonLabel) ? "Run" : buttonLabel;
            _action = action;
        }

        public override void Render(UiBuilder body)
        {
            body.ControlRow(Label, Description, slot =>
            {
                slot.Button(_buttonLabel, Run, width: 180f);
                slot.Flexible();
            });
        }

        private void Run() => SdkLog.SafeInvoke($"Settings action '{Label}'", _action);
    }
}
