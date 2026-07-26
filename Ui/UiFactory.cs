using NeonNightSDK.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonNightSDK.Ui
{
    // Low-level plumbing every piece of UI in this kit needs: the canvas, the font, the
    // EventSystem, and the RectTransform boilerplate. Internal on purpose — mods talk to
    // UiBuilder / UiWindow / UiOverlay, not to this.
    internal static class UiFactory
    {
        // 1920x1080 reference so the UI scales with the player's resolution instead of
        // becoming a postage stamp on 4K. Same value the existing hand-rolled windows used.
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        private static Font _font;

        // Unity 2022's built-in UI font. In older Unity this was "Arial.ttf"; asking for the
        // wrong name returns null and every Text silently renders nothing, so the result is
        // checked once and cached.
        internal static Font Font
        {
            get
            {
                if (_font != null) return _font;

                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    if (_font == null)
                        SdkLog.Error("Ui: could not load a built-in font — all text will be invisible.");
                }

                return _font;
            }
        }

        // uGUI buttons and input fields do nothing without an EventSystem in the scene. The
        // game has one during normal play, but relying on that is how you get a window that
        // works everywhere except the one scene that doesn't. Cheap to guarantee.
        internal static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("NeonNightSDK_EventSystem");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();

            SdkLog.Info("Ui: no EventSystem in the scene, created one (buttons would not respond otherwise).");
        }

        internal static GameObject CreateCanvas(string name, int sortingOrder, bool persistAcrossScenes, bool interactive)
        {
            var go = new GameObject(name);
            if (persistAcrossScenes) Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // 0.5 = balance width and height. With the default (0 = match width only) the UI
            // overflows vertically on ultrawide monitors.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                EnsureEventSystem();
            }

            return go;
        }

        internal static RectTransform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // Fill the parent completely.
        internal static RectTransform Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        internal static Image AddBackground(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            // A fully transparent background still eats clicks, which silently breaks anything
            // underneath. Only raycast when there's something to see.
            image.raycastTarget = color.a > 0.001f;
            return image;
        }

        internal static VerticalLayoutGroup AddVerticalLayout(RectTransform rect, float padding, float spacing)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return layout;
        }

        internal static HorizontalLayoutGroup AddHorizontalLayout(RectTransform rect, float padding, float spacing)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        internal static LayoutElement SetSize(RectTransform rect, float? width, float? height,
            float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            var element = rect.gameObject.GetComponent<LayoutElement>() ??
                          rect.gameObject.AddComponent<LayoutElement>();

            if (width.HasValue)
            {
                element.minWidth = width.Value;
                element.preferredWidth = width.Value;
            }

            if (height.HasValue)
            {
                element.minHeight = height.Value;
                element.preferredHeight = height.Value;
            }

            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        // Wraps `parent` in a vertical ScrollRect and returns the CONTENT transform — the
        // thing you actually parent widgets to.
        //
        // The three-object dance (ScrollRect -> Viewport(mask) -> Content) is what uGUI
        // requires; getting any of the anchors wrong produces content that either doesn't
        // scroll or scrolls into infinity, which is the single most common way hand-rolled
        // Unity UI breaks.
        internal static RectTransform MakeScrollable(RectTransform parent, UiTheme theme, out ScrollRect scrollRect)
        {
            var viewport = Stretch(NewChild(parent, "Viewport"));
            // RectMask2D instead of Mask: no extra material, no stencil buffer, and it's the
            // right tool for a rectangular clip.
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = NewChild(viewport, "Content");
            // Top-stretched with a top pivot: the content grows downward as widgets are added,
            // which is what ContentSizeFitter + a vertical layout expect.
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            AddVerticalLayout(content, theme.Padding, theme.Spacing);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect = parent.gameObject.AddComponent<ScrollRect>();
            scrollRect.content = content;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            return content;
        }
    }
}
