using System;
using NeonNightSDK.Core;
using UnityEngine;
using UnityEngine.UI;
using UButton = UnityEngine.UI.Button;
using UImage = UnityEngine.UI.Image;
using USlider = UnityEngine.UI.Slider;
using UText = UnityEngine.UI.Text;
using UToggle = UnityEngine.UI.Toggle;

namespace NeonNightSDK.Ui
{
    // Fluent widget builder for one container. This is what turns 70 lines of
    // GameObject/RectTransform/anchor arithmetic into:
    //
    //   body.Title("NeonNet")
    //       .Text("Bem-vindo.")
    //       .Button("Entrar", Entrar);
    //
    // Layout is delegated to uGUI's own layout groups (Vertical/HorizontalLayoutGroup +
    // LayoutElement + ContentSizeFitter). That's the whole trick: hand-rolled UI in this
    // codebase set anchorMin/anchorMax/sizeDelta/anchoredPosition by hand on every widget,
    // which is why adding one element meant repositioning all the others. Here widgets just
    // stack, and the layout group does the arithmetic.
    //
    // Every method returns `this`, so calls chain. Where you need the actual Unity component
    // (to keep a reference and update it later), pass `configure:` — it hands you the
    // component right after creation:
    //
    //   header.Input("buscar...", Buscar, configure: f => _barra = f);
    public sealed class UiBuilder
    {
        // The transform new widgets get parented to.
        public RectTransform Root { get; }

        public UiTheme Theme { get; }

        internal UiBuilder(RectTransform root, UiTheme theme)
        {
            Root = root;
            Theme = theme ?? UiTheme.Default;
        }

        // ---- text ---------------------------------------------------------------------

        public UiBuilder Title(string text, Action<UText> configure = null) =>
            Text(text, Theme.FontSizeTitle, Theme.Text, FontStyle.Bold, configure: configure);

        public UiBuilder Heading(string text, Action<UText> configure = null) =>
            Text(text, Theme.FontSizeHeading, Theme.Text, FontStyle.Bold, configure: configure);

        public UiBuilder Muted(string text, Action<UText> configure = null) =>
            Text(text, Theme.FontSizeSmall, Theme.TextMuted, configure: configure);

        public UiBuilder Text(
            string text,
            int? size = null,
            Color? color = null,
            FontStyle style = FontStyle.Normal,
            TextAnchor align = TextAnchor.UpperLeft,
            Action<UText> configure = null)
        {
            var rect = UiFactory.NewChild(Root, "Text");

            var label = rect.gameObject.AddComponent<UText>();
            label.font = UiFactory.Font;
            label.text = text ?? string.Empty;
            label.fontSize = size ?? Theme.FontSizeBody;
            label.fontStyle = style;
            label.color = color ?? Theme.Text;
            label.alignment = align;
            // Wrap horizontally, grow vertically. Text implements ILayoutElement, so the
            // layout group reads preferredHeight from the wrapped result and the container
            // grows to fit — no manual height needed.
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            configure?.Invoke(label);
            return this;
        }

        // ---- interaction --------------------------------------------------------------

        public UiBuilder Button(string label, Action onClick, float? width = null,
            Action<UButton> configure = null)
        {
            var rect = UiFactory.NewChild(Root, $"Button_{label}");
            UiFactory.AddBackground(rect, Theme.ButtonBackground);

            var button = rect.gameObject.AddComponent<UButton>();
            button.targetGraphic = rect.GetComponent<UImage>();

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Multiply(Theme.ButtonHover, Theme.ButtonBackground);
            colors.pressedColor = Multiply(Theme.ButtonPressed, Theme.ButtonBackground);
            colors.selectedColor = Color.white;
            button.colors = colors;

            // A layout group ON the button makes its preferred width follow the label, so a
            // button in a horizontal row sizes to its text instead of collapsing to zero.
            // In a vertical container the parent force-expands it to full width anyway.
            var inner = UiFactory.AddHorizontalLayout(rect, 0f, 0f);
            inner.padding = new RectOffset(12, 12, 0, 0);
            inner.childAlignment = TextAnchor.MiddleCenter;
            inner.childForceExpandWidth = true;

            UiFactory.SetSize(rect, width, Theme.ButtonHeight);

            var textRect = UiFactory.NewChild(rect, "Label");
            var text = textRect.gameObject.AddComponent<UText>();
            text.font = UiFactory.Font;
            text.text = label ?? string.Empty;
            text.fontSize = Theme.FontSizeBody;
            text.color = Theme.ButtonText;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;

            if (onClick != null)
                button.onClick.AddListener(() => SdkLog.SafeInvoke($"Ui button '{label}'", onClick));

            configure?.Invoke(button);
            return this;
        }

        // A button that looks like a hyperlink — no background, accent colour. This is what
        // a page of links in an in-game browser is made of.
        public UiBuilder Link(string label, Action onClick, Action<UButton> configure = null)
        {
            var rect = UiFactory.NewChild(Root, $"Link_{label}");
            // Transparent but still clickable: unlike AddBackground's rule, a link NEEDS to
            // receive raycasts, so the raycastTarget is forced back on.
            var image = UiFactory.AddBackground(rect, new Color(0f, 0f, 0f, 0f));
            image.raycastTarget = true;

            var button = rect.gameObject.AddComponent<UButton>();
            button.targetGraphic = image;

            // A HorizontalLayoutGroup on `rect` itself (same trick Button() uses) gives it a
            // real preferred width — the sum of its children's own preferred sizes. Without
            // one, `rect` has no ILayoutElement of its own (Image/Button don't count), so
            // LayoutUtility reports 0 width for it inside a horizontal Row (a vertical
            // Column/body survives this by force-expanding width regardless) — the label's
            // Text then wraps at ~0px, one character per line.
            var inner = UiFactory.AddHorizontalLayout(rect, 0f, 0f);
            inner.childForceExpandWidth = true;

            var textRect = UiFactory.NewChild(rect, "Label");
            var text = textRect.gameObject.AddComponent<UText>();
            text.font = UiFactory.Font;
            text.text = label ?? string.Empty;
            text.fontSize = Theme.FontSizeBody;
            text.color = Theme.Accent;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            UiFactory.SetSize(rect, null, Theme.FontSizeBody + 10f);

            if (onClick != null)
                button.onClick.AddListener(() => SdkLog.SafeInvoke($"Ui link '{label}'", onClick));

            configure?.Invoke(button);
            return this;
        }

        // Single-line text entry. onSubmit fires on Enter (InputField.onSubmit), which is the
        // behaviour an address bar or a search box wants — onEndEdit would also fire whenever
        // the field merely loses focus.
        public UiBuilder Input(string placeholder, Action<string> onSubmit,
            float? width = null, Action<InputField> configure = null)
        {
            var rect = UiFactory.NewChild(Root, "Input");
            UiFactory.AddBackground(rect, Theme.InputBackground);
            UiFactory.SetSize(rect, width, Theme.ButtonHeight, flexibleWidth: width.HasValue ? -1f : 1f);

            var field = rect.gameObject.AddComponent<InputField>();

            var textRect = UiFactory.Stretch(UiFactory.NewChild(rect, "Text"));
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
            var text = textRect.gameObject.AddComponent<UText>();
            text.font = UiFactory.Font;
            text.fontSize = Theme.FontSizeBody;
            text.color = Theme.Text;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var placeholderRect = UiFactory.Stretch(UiFactory.NewChild(rect, "Placeholder"));
            placeholderRect.offsetMin = new Vector2(10f, 0f);
            placeholderRect.offsetMax = new Vector2(-10f, 0f);
            var placeholderText = placeholderRect.gameObject.AddComponent<UText>();
            placeholderText.font = UiFactory.Font;
            placeholderText.text = placeholder ?? string.Empty;
            placeholderText.fontSize = Theme.FontSizeBody;
            placeholderText.color = Theme.TextMuted;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.raycastTarget = false;

            field.textComponent = text;
            field.placeholder = placeholderText;
            field.lineType = InputField.LineType.SingleLine;

            if (onSubmit != null)
                field.onSubmit.AddListener(value => SdkLog.SafeInvoke("Ui input submit", () => onSubmit(value)));

            configure?.Invoke(field);
            return this;
        }

        // ---- form controls ------------------------------------------------------------
        //
        // All three share the same shape — a label on the left, the control on the right, an
        // optional muted explanation underneath — because that's what makes a page of options
        // read as a table instead of a pile of widgets. The shape lives in ControlRow() and
        // each control only builds its own right-hand side.

        // On/off checkbox.
        //
        //   body.Toggle("Ativar fome", Cfg.Fome, v => Cfg.Fome = v, "Zoey fica com fome com o tempo.");
        public UiBuilder Toggle(string label, bool value, Action<bool> onChanged,
            string description = null, Action<UToggle> configure = null)
        {
            return ControlRow(label, description, slot =>
            {
                var box = UiFactory.NewChild(slot.Root, "Toggle");
                var background = UiFactory.AddBackground(box, Theme.ControlOff);
                background.raycastTarget = true;
                UiFactory.SetSize(box, Theme.ControlHeight, Theme.ControlHeight, flexibleWidth: 0f);

                // Inset so the checkmark reads as "inside the box" instead of covering it.
                var mark = UiFactory.Stretch(UiFactory.NewChild(box, "Checkmark"));
                mark.offsetMin = new Vector2(5f, 5f);
                mark.offsetMax = new Vector2(-5f, -5f);
                var markImage = UiFactory.AddBackground(mark, Theme.ControlOn);
                markImage.raycastTarget = false;

                var toggle = box.gameObject.AddComponent<UToggle>();
                toggle.targetGraphic = background;
                // `graphic` is the one uGUI shows/hides with the state — that's the checkmark,
                // not the box. Wiring these two the other way round is why hand-rolled toggles
                // tend to come out inverted.
                toggle.graphic = markImage;
                toggle.isOn = value;

                if (onChanged != null)
                    toggle.onValueChanged.AddListener(v =>
                        SdkLog.SafeInvoke($"Ui toggle '{label}'", () => onChanged(v)));

                // Keeps the box left-aligned in its slot instead of being stretched or centred
                // by the row's layout group.
                slot.Flexible();

                configure?.Invoke(toggle);
            });
        }

        // Numeric slider with a live readout.
        //
        //   body.Slider("Taxa de fadiga", 0f, 5f, Cfg.Taxa, v => Cfg.Taxa = v);
        //
        // wholeNumbers snaps to integers. `format` controls the readout only (default: one
        // decimal, or no decimals when wholeNumbers).
        public UiBuilder Slider(string label, float min, float max, float value,
            Action<float> onChanged, bool wholeNumbers = false, string description = null,
            Func<float, string> format = null, Action<USlider> configure = null)
        {
            var readout = format ?? (v => v.ToString(wholeNumbers ? "0" : "0.0",
                System.Globalization.CultureInfo.InvariantCulture));

            return ControlRow(label, description, slot =>
            {
                var root = UiFactory.NewChild(slot.Root, "Slider");
                UiFactory.SetSize(root, null, Theme.ControlHeight, flexibleWidth: 1f);

                // uGUI's Slider drives fillRect's and handleRect's anchors itself; all it needs
                // from us is the three-object hierarchy with everything stretched.
                var track = UiFactory.Stretch(UiFactory.NewChild(root, "Track"));
                track.offsetMin = new Vector2(0f, Theme.ControlHeight * 0.35f);
                track.offsetMax = new Vector2(0f, -Theme.ControlHeight * 0.35f);
                UiFactory.AddBackground(track, Theme.ControlTrack);

                var fillArea = UiFactory.Stretch(UiFactory.NewChild(root, "FillArea"));
                fillArea.offsetMin = new Vector2(0f, Theme.ControlHeight * 0.35f);
                fillArea.offsetMax = new Vector2(-Theme.ControlHeight, -Theme.ControlHeight * 0.35f);
                var fill = UiFactory.Stretch(UiFactory.NewChild(fillArea, "Fill"));
                var fillImage = UiFactory.AddBackground(fill, Theme.ControlOn);
                fillImage.raycastTarget = false;

                var handleArea = UiFactory.Stretch(UiFactory.NewChild(root, "HandleArea"));
                var handle = UiFactory.NewChild(handleArea, "Handle");
                handle.sizeDelta = new Vector2(Theme.ControlHeight * 0.6f, 0f);
                var handleImage = UiFactory.AddBackground(handle, Theme.ControlHandle);
                handleImage.raycastTarget = true;

                var slider = root.gameObject.AddComponent<USlider>();
                slider.fillRect = fill;
                slider.handleRect = handle;
                slider.targetGraphic = handleImage;
                slider.direction = USlider.Direction.LeftToRight;
                slider.minValue = min;
                slider.maxValue = max;
                slider.wholeNumbers = wholeNumbers;
                slider.value = Mathf.Clamp(value, min, max);

                UText valueLabel = null;
                slot.Text(readout(slider.value), Theme.FontSizeSmall, Theme.TextMuted,
                    align: TextAnchor.MiddleRight,
                    configure: t =>
                    {
                        valueLabel = t;
                        UiFactory.SetSize((RectTransform)t.transform, 60f, null, flexibleWidth: 0f);
                    });

                slider.onValueChanged.AddListener(v =>
                {
                    if (valueLabel != null) valueLabel.text = readout(v);
                    if (onChanged != null)
                        SdkLog.SafeInvoke($"Ui slider '{label}'", () => onChanged(v));
                });

                configure?.Invoke(slider);
            });
        }

        // Cycles through a fixed list of choices: `<  Normal  >`.
        //
        //   body.Select("Dificuldade", new[]{"Fácil","Normal","Difícil"}, 1, i => Cfg.Dif = i);
        //
        // A cycler rather than a uGUI Dropdown on purpose: Dropdown needs a template hierarchy
        // (blocker, scroll view, item prefab) that has to be built by hand at runtime and then
        // renders BELOW other canvases, while a cycler is three widgets, works with a
        // controller, and matches how console-style game menus present the same choice.
        public UiBuilder Select(string label, string[] options, int index, Action<int> onChanged,
            string description = null)
        {
            if (options == null || options.Length == 0)
            {
                SdkLog.Warn($"UiBuilder.Select('{label}'): no options, skipping.");
                return this;
            }

            return ControlRow(label, description, slot =>
            {
                var current = Mathf.Clamp(index, 0, options.Length - 1);
                UText valueLabel = null;

                // Wraps around at both ends, so the control never dead-ends on a short list.
                void Step(int delta)
                {
                    current = (current + delta + options.Length) % options.Length;
                    if (valueLabel != null) valueLabel.text = options[current];
                    if (onChanged != null)
                        SdkLog.SafeInvoke($"Ui select '{label}'", () => onChanged(current));
                }

                slot.Button("<", () => Step(-1), width: 32f);
                slot.Text(options[current], Theme.FontSizeBody, Theme.Text,
                    align: TextAnchor.MiddleCenter,
                    configure: t =>
                    {
                        valueLabel = t;
                        UiFactory.SetSize((RectTransform)t.transform, null, null, flexibleWidth: 1f);
                    });
                slot.Button(">", () => Step(1), width: 32f);
            });
        }

        // The shared skeleton behind Toggle / Slider / Select — and the extension point for a
        // mod that needs a control this kit doesn't ship: pass your own `build` and you get the
        // same alignment as everything else on the page.
        public UiBuilder ControlRow(string label, string description, Action<UiBuilder> build)
        {
            var group = UiFactory.NewChild(Root, $"Control_{label}");
            UiFactory.AddVerticalLayout(group, 0f, 2f);
            UiFactory.SetSize(group, null, null, flexibleWidth: 1f);

            var row = UiFactory.NewChild(group, "Row");
            UiFactory.AddHorizontalLayout(row, 0f, Theme.Spacing);
            UiFactory.SetSize(row, null, Theme.ButtonHeight, flexibleWidth: 1f);

            var labelRect = UiFactory.NewChild(row, "Label");
            var labelText = labelRect.gameObject.AddComponent<UText>();
            labelText.font = UiFactory.Font;
            labelText.text = label ?? string.Empty;
            labelText.fontSize = Theme.FontSizeBody;
            labelText.color = Theme.Text;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.raycastTarget = false;
            UiFactory.SetSize(labelRect, Theme.LabelWidth, null, flexibleWidth: 0f);

            var slot = UiFactory.NewChild(row, "Slot");
            UiFactory.AddHorizontalLayout(slot, 0f, Theme.Spacing);
            UiFactory.SetSize(slot, null, null, flexibleWidth: 1f);

            SdkLog.SafeInvoke($"UiBuilder.ControlRow('{label}')", () => build?.Invoke(new UiBuilder(slot, Theme)));

            if (!string.IsNullOrEmpty(description))
                new UiBuilder(group, Theme).Muted(description);

            return this;
        }

        // ---- media and spacing --------------------------------------------------------

        public UiBuilder Image(Sprite sprite, float? height = null, float? width = null,
            Action<UImage> configure = null)
        {
            if (sprite == null)
            {
                SdkLog.Warn("UiBuilder.Image: sprite is null, skipping.");
                return this;
            }

            var rect = UiFactory.NewChild(Root, $"Image_{sprite.name}");
            var image = rect.gameObject.AddComponent<UImage>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // Given only one dimension, derive the other from the sprite's aspect ratio so
            // images don't come out squashed.
            var aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
            var h = height ?? (width.HasValue ? width.Value / aspect : 120f);
            var w = width ?? h * aspect;

            UiFactory.SetSize(rect, w, h);

            configure?.Invoke(image);
            return this;
        }

        public UiBuilder Spacer(float height = 8f)
        {
            var rect = UiFactory.NewChild(Root, "Spacer");
            UiFactory.SetSize(rect, null, height);
            return this;
        }

        // Eats all remaining space. In a Row this pushes what follows to the right — the
        // standard way to put a close button at the far end of a header.
        public UiBuilder Flexible()
        {
            var rect = UiFactory.NewChild(Root, "Flexible");
            UiFactory.SetSize(rect, 0f, 0f, flexibleWidth: 1f, flexibleHeight: 1f);
            return this;
        }

        public UiBuilder Separator()
        {
            var rect = UiFactory.NewChild(Root, "Separator");
            UiFactory.AddBackground(rect, Theme.Separator);
            UiFactory.SetSize(rect, null, 1f);
            return this;
        }

        // ---- nesting ------------------------------------------------------------------

        // Lays its children out left-to-right. Use Flexible() inside to push things apart.
        public UiBuilder Row(Action<UiBuilder> build, float? height = null, float spacing = -1f)
        {
            var rect = UiFactory.NewChild(Root, "Row");
            UiFactory.AddHorizontalLayout(rect, 0f, spacing < 0f ? Theme.Spacing : spacing);
            UiFactory.SetSize(rect, null, height ?? Theme.ButtonHeight);

            build?.Invoke(new UiBuilder(rect, Theme));
            return this;
        }

        // Lays its children out top-to-bottom. Nest inside a Row to get columns.
        public UiBuilder Column(Action<UiBuilder> build, float? width = null, float spacing = -1f)
        {
            var rect = UiFactory.NewChild(Root, "Column");
            UiFactory.AddVerticalLayout(rect, 0f, spacing < 0f ? Theme.Spacing : spacing);
            UiFactory.SetSize(rect, width, null,
                flexibleWidth: width.HasValue ? -1f : 1f, flexibleHeight: -1f);

            build?.Invoke(new UiBuilder(rect, Theme));
            return this;
        }

        // Escape hatch: raw access to a fresh child RectTransform, for anything this kit
        // doesn't wrap. Add whatever components you like; the layout group still positions it.
        public UiBuilder Custom(string name, Action<RectTransform> build, float? width = null, float? height = null)
        {
            var rect = UiFactory.NewChild(Root, name);
            if (width.HasValue || height.HasValue) UiFactory.SetSize(rect, width, height);

            SdkLog.SafeInvoke($"UiBuilder.Custom('{name}')", () => build?.Invoke(rect));
            return this;
        }

        // ---- content management -------------------------------------------------------

        // Removes every widget in this container. This is how you navigate: clear the body
        // and build the next page into it.
        public UiBuilder Clear()
        {
            for (var i = Root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(Root.GetChild(i).gameObject);

            return this;
        }

        private static Color Multiply(Color target, Color background)
        {
            // Unity's Button tints targetGraphic.color BY the state colour, so a literal
            // "hover colour" has to be expressed relative to the background to come out right.
            return new Color(
                background.r > 0.001f ? target.r / background.r : 1f,
                background.g > 0.001f ? target.g / background.g : 1f,
                background.b > 0.001f ? target.b / background.b : 1f,
                1f);
        }
    }
}
