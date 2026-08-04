using UnityEngine;

namespace NeonNightSDK.Ui
{
    // Every color, size and spacing the UI kit uses, in one place.
    //
    // Two reasons this exists instead of hardcoded values scattered through the widgets:
    // a mod can restyle its whole UI by changing one object, and two different mods can look
    // like they belong to the same game by sharing UiTheme.Default.
    //
    // Copy and tweak:
    //   var theme = UiTheme.Default.Clone();
    //   theme.Accent = new Color(0f, 1f, 0.8f);
    //   HudKit.Window("Net", 1200, 800, theme: theme);
    public sealed class UiTheme
    {
        // Dark, slightly blue-black, neon accent — reads well over the game's night-city art
        // without fighting it. Everything here is overridable.
        public static readonly UiTheme Default = new UiTheme();

        public Color WindowBackground = new Color(0.06f, 0.07f, 0.10f, 0.97f);
        public Color HeaderBackground = new Color(0.10f, 0.12f, 0.17f, 1f);
        public Color SidebarBackground = new Color(0.08f, 0.09f, 0.13f, 1f);
        public Color BodyBackground = new Color(0.06f, 0.07f, 0.10f, 1f);
        public Color FooterBackground = new Color(0.10f, 0.12f, 0.17f, 1f);

        // Full-screen dim behind a modal window.
        public Color ModalBackdrop = new Color(0f, 0f, 0f, 0.6f);

        public Color Text = new Color(0.88f, 0.90f, 0.95f, 1f);
        public Color TextMuted = new Color(0.55f, 0.58f, 0.66f, 1f);
        public Color Accent = new Color(0.25f, 0.85f, 0.95f, 1f);

        public Color ButtonBackground = new Color(0.16f, 0.19f, 0.26f, 1f);
        public Color ButtonHover = new Color(0.22f, 0.27f, 0.36f, 1f);
        public Color ButtonPressed = new Color(0.12f, 0.15f, 0.20f, 1f);
        public Color ButtonText = new Color(0.90f, 0.93f, 0.98f, 1f);

        public Color InputBackground = new Color(0.03f, 0.04f, 0.06f, 1f);
        public Color Separator = new Color(1f, 1f, 1f, 0.10f);

        // Form controls (Toggle / Slider / Select). Off-state deliberately reuses the input
        // background so an unchecked box reads as "empty field" rather than "another button".
        public Color ControlOff = new Color(0.03f, 0.04f, 0.06f, 1f);
        public Color ControlOn = new Color(0.25f, 0.85f, 0.95f, 1f);
        public Color ControlTrack = new Color(0.03f, 0.04f, 0.06f, 1f);
        public Color ControlHandle = new Color(0.88f, 0.90f, 0.95f, 1f);

        public int FontSizeTitle = 30;
        public int FontSizeHeading = 22;
        public int FontSizeBody = 17;
        public int FontSizeSmall = 14;

        public float Padding = 14f;
        public float Spacing = 8f;
        public float ButtonHeight = 34f;
        public float HeaderHeight = 52f;
        public float FooterHeight = 36f;

        // Width reserved for the label of a form control, so every row in a settings page
        // lines its control up at the same x — the thing that makes a list of options read as
        // a table instead of a pile of widgets.
        public float LabelWidth = 260f;
        public float ControlHeight = 26f;

        public UiTheme Clone() => (UiTheme)MemberwiseClone();
    }
}
