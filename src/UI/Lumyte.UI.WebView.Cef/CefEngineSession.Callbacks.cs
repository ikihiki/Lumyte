using Xilium.CefGlue;

namespace Lumyte.UI.WebView.Cef;

internal sealed partial class CefEngineSession
{
    private sealed class Client(CefEngineSession owner) : CefClient
    {
        private readonly Life _life = new(owner);
        private readonly Render _render = new(owner);
        private readonly Load _load = new(owner);
        private readonly Display _display = new(owner);
        private readonly Focus _focus = new(owner);
        private readonly Requests _requests = new(owner);
        private readonly Permissions _permissions = new();
        private readonly Downloads _downloads = new();
        private readonly Dialogs _dialogs = new();
        private readonly Menus _menus = new();
        private readonly JsDialogs _jsDialogs = new();
        private readonly Printing _printing = new();

        protected override CefLifeSpanHandler GetLifeSpanHandler() => _life;

        protected override CefRenderHandler GetRenderHandler() => _render;

        protected override CefLoadHandler GetLoadHandler() => _load;

        protected override CefDisplayHandler GetDisplayHandler() => _display;

        protected override CefFocusHandler GetFocusHandler() => _focus;

        protected override CefRequestHandler GetRequestHandler() => _requests;

        protected override CefPermissionHandler GetPermissionHandler() => _permissions;

        protected override CefDownloadHandler GetDownloadHandler() => _downloads;

        protected override CefDialogHandler GetDialogHandler() => _dialogs;

        protected override CefContextMenuHandler GetContextMenuHandler() => _menus;

        protected override CefJSDialogHandler GetJSDialogHandler() => _jsDialogs;

        protected override CefPrintHandler GetPrintHandler() => _printing;
    }

    private sealed class Life(CefEngineSession owner) : CefLifeSpanHandler
    {
        protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess) => true;

        protected override void OnAfterCreated(CefBrowser browser)
        {
            lock (owner._gate)
            {
                owner._browser = browser;
                using CefBrowserHost host = browser.GetHost();
                host.SetAudioMuted(true);
                if (!owner._closing)
                {
                    owner._snapshot = owner._snapshot with { State = UiWebViewState.Ready };
                }

                owner.Changed();
                owner.PostWork();
            }
        }

        protected override bool DoClose(CefBrowser browser) => false;

        protected override void OnBeforeClose(CefBrowser browser)
        {
            lock (owner._gate)
            {
                owner._browser = null;
                owner._snapshot = owner._snapshot with { State = UiWebViewState.Closed, Loading = false, PopupVisible = false };
                owner.Changed();
                browser.Dispose();
            }
        }
    }

    private sealed class Render(CefEngineSession owner) : CefRenderHandler
    {
        // Accessibility and native IME are explicitly unavailable. Shared textures are disabled.
        protected override CefAccessibilityHandler GetAccessibilityHandler() => null!;

        protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange range, CefRectangle[] bounds)
        {
        }

        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
        {
        }

        protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirty, IntPtr handle)
        {
            using CefBrowserHost host = browser.GetHost();
            host.Invalidate(CefPaintElementType.View);
        }

        protected override void GetViewRect(CefBrowser browser, out CefRectangle rect)
        {
            lock (owner._gate)
            {
                rect = new(0, 0, Math.Max(1, owner._viewport.Width), Math.Max(1, owner._viewport.Height));
            }
        }

        protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo info)
        {
            lock (owner._gate)
            {
                info.DeviceScaleFactor = owner._viewport.Scale;
                info.Depth = 32;
                info.DepthPerComponent = 8;
                info.Rectangle = new(0, 0, Math.Max(1, owner._viewport.Width), Math.Max(1, owner._viewport.Height));
                info.AvailableRectangle = info.Rectangle;
                return true;
            }
        }

        protected override void OnPopupShow(CefBrowser browser, bool show)
        {
            lock (owner._gate)
            {
                owner._snapshot = owner._snapshot with { PopupVisible = show };
                if (!show)
                {
                    owner.PopupPixels.Resize(owner._viewport.Generation);
                }

                owner.Changed();
            }
        }

        protected override void OnPopupSize(CefBrowser browser, CefRectangle rect)
        {
            lock (owner._gate)
            {
                owner._snapshot = owner._snapshot with { PopupX = rect.X, PopupY = rect.Y, PopupWidth = rect.Width, PopupHeight = rect.Height };
                owner.PopupPixels.Resize(owner._viewport.Generation);
                owner.Changed();
            }
        }

        protected override unsafe void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
        {
            lock (owner._gate)
            {
                if (owner._closing || owner._renderGeneration != owner._viewport.Generation || !owner._viewport.Visible || buffer == IntPtr.Zero || width <= 0 || height <= 0)
                {
                    return;
                }

                bool popup = type == CefPaintElementType.Popup;
                int expectedWidth = (int)Math.Ceiling((popup ? owner._snapshot.PopupWidth : owner._viewport.Width) * (double)owner._viewport.Scale);
                int expectedHeight = (int)Math.Ceiling((popup ? owner._snapshot.PopupHeight : owner._viewport.Height) * (double)owner._viewport.Scale);
                if (width != expectedWidth || height != expectedHeight || (popup && !owner._snapshot.PopupVisible))
                {
                    return;
                }

                try
                {
                    int bytes = checked(width * height * 4);
                    WebViewPixelMailbox mailbox = popup ? owner.PopupPixels : owner.ViewPixels;
                    if (mailbox.Publish(new ReadOnlySpan<byte>(buffer.ToPointer(), bytes), width, height, owner._viewport.Generation))
                    {
                        owner.Changed();
                    }
                }
                catch (Exception error) when (error is OverflowException or OutOfMemoryException)
                {
                    owner._snapshot = owner._snapshot with { State = UiWebViewState.Faulted, FailureCode = "cef-pixel-allocation-failed" };
                    owner.Changed();
                }
            }
        }
    }

    private sealed class Load(CefEngineSession owner) : CefLoadHandler
    {
        protected override void OnLoadingStateChange(CefBrowser browser, bool loading, bool back, bool forward)
        {
            lock (owner._gate)
            {
                if (owner._closing)
                {
                    return;
                }

                // Query browser-global current loading state, not an old frame's OnLoadEnd.
                owner._snapshot = owner._snapshot with { Loading = browser.IsLoading, Back = browser.CanGoBack, Forward = browser.CanGoForward };
                owner.Changed();
            }
        }
    }

    private sealed class Display(CefEngineSession owner) : CefDisplayHandler
    {
        protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url)
        {
            if (!frame.IsMain || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !Allowed(url))
            {
                return;
            }

            lock (owner._gate)
            {
                if (!owner._closing)
                {
                    owner._snapshot = owner._snapshot with { Uri = uri };
                    owner.Changed();
                }
            }
        }

        protected override bool OnConsoleMessage(CefBrowser browser, CefLogSeverity level, string message, string source, int line) => true;

        protected override bool OnTooltip(CefBrowser browser, string text) => true;
    }

    private sealed class Focus(CefEngineSession owner) : CefFocusHandler
    {
        protected override bool OnSetFocus(CefBrowser browser, CefFocusSource source)
        {
            lock (owner._gate)
            {
                return !owner._focusAdmission.Focused || owner._closing;
            }
        }

        protected override void OnTakeFocus(CefBrowser browser, bool next)
        {
            lock (owner._gate)
            {
                if (!owner._focusAdmission.Focused || owner._closing)
                {
                    return;
                }

                owner._snapshot = owner._snapshot with { TakeFocusForward = next, FocusRequest = owner._focusAdmission.Request };
                owner.Changed();
            }
        }
    }
}
