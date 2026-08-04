using System;
using System.Collections.Generic;
using NeonNightSDK.Core;
using UnityEngine;
using UImage = UnityEngine.UI.Image;
using UText = UnityEngine.UI.Text;

namespace NeonNightSDK.Ui
{
    public enum ScreenCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    // A always-on HUD pinned to a corner of the screen — the NeedsHud case.
    //
    // The point of StatIcon is that you don't write a refresh loop: you hand over a function
    // that returns 0..1 and the overlay polls it and repaints. That's what turns 70 lines of
    // canvas/anchor/refresh code into two.
    //
    // Created through HudKit.Overlay(...). Survives scene changes (DontDestroyOnLoad) and
    // hides itself in the main menu by default.
    public sealed class UiOverlay
    {
        private sealed class Binding
        {
            internal UImage Icon;
            internal UText Label;
            internal Func<float> Value;
            internal Func<float, string> Format;
        }

        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly ScheduledTask _refreshTask;
        private readonly Action<string> _sceneHandler;

        private GameObject _canvas;
        private RectTransform _panel;

        public UiBuilder Content { get; private set; }

        public UiTheme Theme { get; }

        public bool IsVisible => _canvas != null && _canvas.activeSelf;

        // Below this fraction the icon is solid red and stops interpolating — a "you are in
        // trouble" signal that doesn't drift back toward orange.
        public float CriticalThreshold { get; set; } = 0.10f;

        internal UiOverlay(string name, ScreenCorner corner, UiTheme theme, int sortingOrder,
            Vector2 margin, float refreshInterval, bool hideInMainMenu)
        {
            Theme = theme ?? UiTheme.Default;

            // interactive: false — a HUD must never eat clicks. With a GraphicRaycaster it
            // would silently swallow input meant for the game or for a window on top.
            _canvas = UiFactory.CreateCanvas(name, sortingOrder, persistAcrossScenes: true, interactive: false);

            _panel = UiFactory.NewChild(_canvas.transform, "Panel");
            ApplyCorner(_panel, corner, margin);
            UiFactory.AddVerticalLayout(_panel, 0f, Theme.Spacing);

            var fitter = _panel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            Content = new UiBuilder(_panel, Theme);

            // unscaledTime: true is NOT optional here. Asuna.UI.TabMenu (the inventory/tab
            // screen) sets Time.timeScale = 0f while it is open, so a scaled-time refresh
            // simply stops ticking: the HUD freezes on whatever value it had when the player
            // opened the inventory, and only catches up after they close it. That looked
            // exactly like "eating food doesn't restore hunger" — the stat had already
            // changed, the readout hadn't. A display poll must always run on wall-clock time.
            _refreshTask = Scheduler
                .Every(refreshInterval, Refresh, unscaledTime: true)
                .Named($"{name}.Refresh");

            if (hideInMainMenu)
            {
                _sceneHandler = scene =>
                {
                    if (Scenes.IsMainMenu(scene)) Hide();
                    else Show();
                };
                SdkEvents.OnSceneLoaded += _sceneHandler;

                // Constructed while already sitting in the menu? Start hidden.
                if (Scenes.IsMainMenu(Scenes.Active)) Hide();
            }
        }

        // One icon + readout bound to a live value.
        //
        //   hud.StatIcon(fome, () => zoey.GetStat("hunger").BaseValue / 100f);
        //
        // `value` must return 0..1. The icon tints green -> yellow -> red as it drains, and
        // the label shows a percentage unless you pass your own `format`.
        public UiOverlay StatIcon(Sprite icon, Func<float> value, Func<float, string> format = null, float size = 48f)
        {
            if (value == null)
            {
                SdkLog.Error("UiOverlay.StatIcon: value function is null, skipping.");
                return this;
            }

            var binding = new Binding { Value = value, Format = format };

            Content.Custom("Stat", slot =>
            {
                UiFactory.AddBackground(slot, new Color(0f, 0f, 0f, 0.45f));
                UiFactory.AddVerticalLayout(slot, 4f, 2f);

                if (icon != null)
                {
                    var iconRect = UiFactory.NewChild(slot, "Icon");
                    var image = iconRect.gameObject.AddComponent<UImage>();
                    image.sprite = icon;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    UiFactory.SetSize(iconRect, size, size);
                    binding.Icon = image;
                }

                var textRect = UiFactory.NewChild(slot, "Value");
                var text = textRect.gameObject.AddComponent<UText>();
                text.font = UiFactory.Font;
                text.fontSize = Theme.FontSizeSmall;
                text.color = Theme.Text;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                UiFactory.SetSize(textRect, size + 8f, 18f);
                binding.Label = text;
            }, width: size + 8f);

            _bindings.Add(binding);
            Refresh();
            return this;
        }

        // Repaints every binding. Called automatically on the refresh interval; call it by
        // hand if you need an immediate update after changing something.
        public void Refresh()
        {
            if (_canvas == null) return;

            foreach (var binding in _bindings)
            {
                float percent;
                try
                {
                    percent = Mathf.Clamp01(binding.Value());
                }
                catch (Exception ex)
                {
                    SdkLog.Error($"UiOverlay: a StatIcon value function threw, showing 0: {ex}");
                    percent = 0f;
                }

                if (binding.Icon != null) binding.Icon.color = ColorForPercent(percent);

                if (binding.Label != null)
                {
                    binding.Label.text = binding.Format != null
                        ? binding.Format(percent)
                        : Mathf.RoundToInt(percent * 100f) + "%";
                }
            }
        }

        public void Show()
        {
            if (_canvas != null) _canvas.SetActive(true);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.SetActive(false);
        }

        public void Destroy()
        {
            _refreshTask?.Cancel();

            if (_sceneHandler != null) SdkEvents.OnSceneLoaded -= _sceneHandler;

            if (_canvas != null)
            {
                UnityEngine.Object.Destroy(_canvas);
                _canvas = null;
            }

            _bindings.Clear();
            Content = null;
            _panel = null;
        }

        private Color ColorForPercent(float percent)
        {
            if (percent <= CriticalThreshold) return Color.red;

            var t = (percent - CriticalThreshold) / (1f - CriticalThreshold);
            return t < 0.5f
                ? Color.Lerp(Color.red, Color.yellow, t * 2f)
                : Color.Lerp(Color.yellow, Color.green, (t - 0.5f) * 2f);
        }

        private static void ApplyCorner(RectTransform rect, ScreenCorner corner, Vector2 margin)
        {
            Vector2 anchor;
            Vector2 offset;

            switch (corner)
            {
                case ScreenCorner.TopRight:
                    anchor = new Vector2(1f, 1f);
                    offset = new Vector2(-margin.x, -margin.y);
                    break;
                case ScreenCorner.BottomLeft:
                    anchor = new Vector2(0f, 0f);
                    offset = new Vector2(margin.x, margin.y);
                    break;
                case ScreenCorner.BottomRight:
                    anchor = new Vector2(1f, 0f);
                    offset = new Vector2(-margin.x, margin.y);
                    break;
                default:
                    anchor = new Vector2(0f, 1f);
                    offset = new Vector2(margin.x, -margin.y);
                    break;
            }

            // Pivot matched to the anchor so the margin pushes the panel INTO the screen from
            // its corner, whichever corner that is.
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
        }
    }
}
