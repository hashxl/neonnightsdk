using System;
using ANToolkit.UI;
using NeonNightSDK.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NeonNightSDK.Ui
{
    // A panel window with the regions a real interface needs: header, sidebar, body, footer.
    //
    // The regions are wired with uGUI layout groups so they behave like you'd expect: the
    // header keeps its height, the sidebar keeps its width, and the body absorbs whatever is
    // left when the window is resized. Sidebar and body scroll on their own.
    //
    //   Panel  (vertical)
    //   ├─ Header                       fixed height
    //   ├─ Middle (horizontal)          takes the remaining height
    //   │   ├─ Sidebar                  fixed width, scrolls
    //   │   └─ Body                     takes the remaining width, scrolls
    //   └─ Footer                       fixed height
    //
    // Create through HudKit.Window(...).
    public sealed class UiWindow
    {
        private readonly string _restraintId;
        private readonly bool _modal;
        private readonly bool _closeOnEscape;
        private readonly bool _useGameCancelStack;

        private GameObject _canvas;
        private Action _escapeHandler;
        private Action<string> _sceneHandler;

        // Region builders. Null when the region wasn't requested (no sidebar, no footer).
        // Append to them directly, or replace their whole contents with SetBody/SetSidebar/...
        public UiBuilder Header { get; private set; }
        public UiBuilder Sidebar { get; private set; }
        public UiBuilder Body { get; private set; }
        public UiBuilder Footer { get; private set; }

        public UiTheme Theme { get; }

        // Canvas GameObject name — also the key HudKit uses to detect a re-open.
        public string Name { get; }

        public bool IsOpen => _canvas != null;

        // Fires once, after the window is torn down.
        public event Action Closed;

        internal ScrollRect BodyScroll { get; private set; }
        internal ScrollRect SidebarScroll { get; private set; }

        internal UiWindow(
            string name,
            string title,
            float width,
            float height,
            UiTheme theme,
            int sortingOrder,
            float sidebarWidth,
            float headerHeight,
            float footerHeight,
            bool modal,
            bool closeButton,
            bool closeOnEscape,
            bool persistAcrossScenes,
            bool useGameCancelStack)
        {
            Theme = theme ?? UiTheme.Default;
            Name = name;
            _modal = modal;
            _useGameCancelStack = useGameCancelStack;
            // The two escape paths are mutually exclusive: with both on, one Escape press would
            // close the window via the raw key poll AND pop the cancel stack, which in a stack
            // of two windows closes both at once.
            _closeOnEscape = closeOnEscape && !useGameCancelStack;
            _restraintId = $"NeonNightSDK.Ui.{name}";

            if (useGameCancelStack) BindGameCancel();

            _canvas = UiFactory.CreateCanvas(name, sortingOrder, persistAcrossScenes, interactive: true);

            if (modal)
            {
                // Full-screen dim that also swallows clicks, so the player can't interact with
                // the world (or another window) through this one.
                var backdrop = UiFactory.Stretch(UiFactory.NewChild(_canvas.transform, "Backdrop"));
                UiFactory.AddBackground(backdrop, Theme.ModalBackdrop).raycastTarget = true;

                PlayerControl.LockPlayer(_restraintId);
            }

            var panel = UiFactory.NewChild(_canvas.transform, "Panel");
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(width, height);
            panel.anchoredPosition = Vector2.zero;
            UiFactory.AddBackground(panel, Theme.WindowBackground);

            // Zero padding/spacing here: the regions are flush against each other and each one
            // applies its own inner padding. Otherwise you get a visible seam of window
            // background between the header and the body.
            UiFactory.AddVerticalLayout(panel, 0f, 0f);

            if (headerHeight > 0f)
                Header = BuildHeader(panel, title, headerHeight, closeButton);

            var middle = UiFactory.NewChild(panel, "Middle");
            UiFactory.AddHorizontalLayout(middle, 0f, 0f);
            // flexibleHeight 1 = "absorb everything the fixed regions didn't take".
            UiFactory.SetSize(middle, null, 0f, flexibleWidth: 1f, flexibleHeight: 1f);

            if (sidebarWidth > 0f)
            {
                var sidebar = UiFactory.NewChild(middle, "Sidebar");
                UiFactory.AddBackground(sidebar, Theme.SidebarBackground);
                UiFactory.SetSize(sidebar, sidebarWidth, null, flexibleWidth: 0f, flexibleHeight: 1f);

                var content = UiFactory.MakeScrollable(sidebar, Theme, out var sidebarScroll);
                SidebarScroll = sidebarScroll;
                Sidebar = new UiBuilder(content, Theme);
            }

            var body = UiFactory.NewChild(middle, "Body");
            UiFactory.AddBackground(body, Theme.BodyBackground);
            UiFactory.SetSize(body, null, null, flexibleWidth: 1f, flexibleHeight: 1f);

            var bodyContent = UiFactory.MakeScrollable(body, Theme, out var bodyScroll);
            BodyScroll = bodyScroll;
            Body = new UiBuilder(bodyContent, Theme);

            if (footerHeight > 0f)
            {
                var footer = UiFactory.NewChild(panel, "Footer");
                UiFactory.AddBackground(footer, Theme.FooterBackground);
                UiFactory.AddHorizontalLayout(footer, Theme.Padding, Theme.Spacing);
                UiFactory.SetSize(footer, null, footerHeight, flexibleWidth: 1f, flexibleHeight: 0f);
                Footer = new UiBuilder(footer, Theme);
            }

            if (_closeOnEscape)
            {
                _escapeHandler = () =>
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) Close();
                };
                SdkEvents.OnUpdate += _escapeHandler;
            }

            if (!persistAcrossScenes)
            {
                // A window left open across a scene change would float over the loading screen
                // and, if modal, hold an input restraint on a player that no longer exists.
                _sceneHandler = _ => Close();
                SdkEvents.OnSceneLoaded += _sceneHandler;
            }

            SdkLog.Info($"Ui: window '{name}' opened ({width}x{height}" +
                        (modal ? ", modal" : "") + ").");
        }

        // Hands Escape over to the game's own conventions instead of polling the key.
        //
        // NNUICancelStack is what the base game uses for "some UI is open, Cancel should close
        // THAT and nothing else": it keeps a stack of open UIs and pops only the top one. Two
        // windows open, two presses, in the right order — which a raw Input.GetKeyDown poll in
        // each window cannot do, since all of them see the same keypress.
        //
        // MenuManager.CanPause is the second half. ANToolkit.Controllers.PauseInput checks it
        // before toggling the pause menu, so holding a restraint while the window is up stops
        // the same press from also closing (or opening) the pause menu underneath.
        private void BindGameCancel()
        {
            SdkLog.SafeInvoke($"Ui: binding '{Name}' to the game's cancel stack", () =>
            {
                NNUICancelStack.Add(_restraintId, Close);
                MenuManager.CanPause.Add(_restraintId);
            });
        }

        private void UnbindGameCancel()
        {
            SdkLog.SafeInvoke($"Ui: releasing '{Name}' from the game's cancel stack", () =>
            {
                // Remove() is a no-op when the entry is already gone, which is exactly the case
                // when we got here THROUGH the cancel stack popping us.
                NNUICancelStack.Remove(_restraintId);
                MenuManager.CanPause.Remove(_restraintId);
            });
        }

        private UiBuilder BuildHeader(RectTransform panel, string title, float headerHeight, bool closeButton)
        {
            var header = UiFactory.NewChild(panel, "Header");
            UiFactory.AddBackground(header, Theme.HeaderBackground);
            UiFactory.AddHorizontalLayout(header, Theme.Padding, Theme.Spacing);
            UiFactory.SetSize(header, null, headerHeight, flexibleWidth: 1f, flexibleHeight: 0f);

            var builder = new UiBuilder(header, Theme);

            if (!string.IsNullOrEmpty(title))
                builder.Text(title, Theme.FontSizeHeading, Theme.Text, FontStyle.Bold, TextAnchor.MiddleLeft);

            if (closeButton)
            {
                // Flexible spacer pins the close button to the far right regardless of what
                // else gets added to the header later.
                builder.Flexible();
                builder.Button("X", Close, width: 40f);
            }

            return builder;
        }

        // ---- content ------------------------------------------------------------------

        // Clears the region and rebuilds it. This is the navigation primitive: one call per
        // "page" in an in-game browser.
        public UiWindow SetBody(Action<UiBuilder> build) => Replace(Body, build, "Body", scrollToTop: true);

        public UiWindow SetSidebar(Action<UiBuilder> build) => Replace(Sidebar, build, "Sidebar");

        public UiWindow SetHeader(Action<UiBuilder> build) => Replace(Header, build, "Header");

        public UiWindow SetFooter(Action<UiBuilder> build) => Replace(Footer, build, "Footer");

        private UiWindow Replace(UiBuilder region, Action<UiBuilder> build, string what, bool scrollToTop = false)
        {
            if (!IsOpen)
            {
                SdkLog.Warn($"Ui: Set{what} called on a window that is already closed, ignoring.");
                return this;
            }

            if (region == null)
            {
                SdkLog.Error($"Ui: this window has no {what} region — create it with a non-zero " +
                             $"{what.ToLower()} size in HudKit.Window(...).");
                return this;
            }

            region.Clear();
            SdkLog.SafeInvoke($"Ui.Set{what}", () => build?.Invoke(region));

            if (scrollToTop) ScrollBodyToTop();
            return this;
        }

        // Jumps the body back to the top — what you want after navigating to a new page.
        // Deferred by one frame on purpose: the layout hasn't been rebuilt yet at the moment
        // the widgets are created, so setting the position now would be overwritten.
        public UiWindow ScrollBodyToTop()
        {
            if (BodyScroll == null) return this;

            Scheduler.NextFrame(() =>
            {
                if (BodyScroll != null) BodyScroll.verticalNormalizedPosition = 1f;
            });

            return this;
        }

        // ---- lifetime -----------------------------------------------------------------

        public void Close()
        {
            if (_canvas == null) return;

            if (_escapeHandler != null)
            {
                SdkEvents.OnUpdate -= _escapeHandler;
                _escapeHandler = null;
            }

            if (_sceneHandler != null)
            {
                SdkEvents.OnSceneLoaded -= _sceneHandler;
                _sceneHandler = null;
            }

            if (_useGameCancelStack) UnbindGameCancel();

            if (_modal) PlayerControl.UnlockPlayer(_restraintId);

            UnityEngine.Object.Destroy(_canvas);
            _canvas = null;
            Header = Sidebar = Body = Footer = null;
            BodyScroll = null;
            SidebarScroll = null;

            var closed = Closed;
            Closed = null;
            if (closed != null) SdkLog.SafeInvoke("UiWindow.Closed", closed);
        }
    }
}
