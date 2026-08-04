using System;
using NeonNightSDK.Core;
using NeonNightSDK.Ui;
using UnityEngine;
using UnityEngine.UI;
using UButton = UnityEngine.UI.Button;

namespace NeonNightSDK.Settings.Options
{
    // A key binding. Click the button, press a key, done.
    //
    // This rebinds a KEY THE MOD READS ITSELF (Input.GetKeyDown in an OnUpdate handler). It
    // does not touch the game's own InputManager binds — those live in Rewired and belong to
    // the game's Options screen.
    public sealed class KeybindOption : ValueOption<KeyCode>
    {
        private const string CapturingText = "press a key...";

        // Cached once: Enum.GetValues allocates, and the capture loop runs every frame while
        // it's waiting.
        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        // Only one rebind can be in progress at a time (the window shows one page), so a static
        // flag is enough — and it's what SettingsKit checks to stop its own hotkey from being
        // swallowed as the new binding.
        internal static bool IsCapturing { get; private set; }

        private UButton _button;
        private Action _captureHandler;

        public KeybindOption(string key, string label, string description,
            Func<KeyCode> get, Action<KeyCode> set)
            : base(key, label, description, get, set)
        {
        }

        public override void Render(UiBuilder body)
        {
            // Re-rendering (page switch, window reopen) must not leave a capture running
            // against the button that just got destroyed.
            StopCapture();

            body.ControlRow(Label, Description, slot =>
            {
                slot.Button(Describe(Value), BeginCapture, width: 180f, configure: b => _button = b);
                slot.Flexible();
            });
        }

        private void BeginCapture()
        {
            if (_captureHandler != null) return;

            SetButtonText(CapturingText);

            IsCapturing = true;
            _captureHandler = PollForKey;
            SdkEvents.OnUpdate += _captureHandler;
        }

        private void PollForKey()
        {
            // The window can be closed (or the page switched) mid-capture; the button is then a
            // destroyed Unity object and this handler would poll forever.
            if (_button == null)
            {
                StopCapture();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StopCapture();
                SetButtonText(Describe(Value));
                return;
            }

            for (var i = 0; i < AllKeys.Length; i++)
            {
                var code = AllKeys[i];
                if (code == KeyCode.None || IsMouseButton(code)) continue;
                if (!Input.GetKeyDown(code)) continue;

                Value = code;
                StopCapture();
                SetButtonText(Describe(code));
                NotifyChanged();
                return;
            }
        }

        private void StopCapture()
        {
            if (_captureHandler == null) return;

            SdkEvents.OnUpdate -= _captureHandler;
            _captureHandler = null;
            IsCapturing = false;
        }

        private void SetButtonText(string text)
        {
            if (_button == null) return;

            var label = _button.GetComponentInChildren<Text>();
            if (label != null) label.text = text;
        }

        // The click that STARTS the capture is a mouse-down, and it is still down on the frame
        // the poll first runs — without this, every rebind would instantly resolve to Mouse0.
        private static bool IsMouseButton(KeyCode code) =>
            code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6;

        private static string Describe(KeyCode code) =>
            code == KeyCode.None ? "(none)" : code.ToString();

        protected override string Format(KeyCode value) => value.ToString();

        protected override bool TryParse(string raw, out KeyCode value) =>
            Enum.TryParse(raw, ignoreCase: true, out value);
    }
}
