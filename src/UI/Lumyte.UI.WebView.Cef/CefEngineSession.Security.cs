using Xilium.CefGlue;

namespace Lumyte.UI.WebView.Cef;

internal sealed partial class CefEngineSession
{
    internal static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private sealed class Requests(CefEngineSession owner) : CefRequestHandler
    {
        private readonly Resources _resources = new();

        protected override bool OnBeforeBrowse(CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool redirect) => !Allowed(request.Url);

        protected override bool OnOpenUrlFromTab(CefBrowser browser, CefFrame frame, string url, CefWindowOpenDisposition disposition, bool userGesture) => true;

        protected override bool OnCertificateError(CefBrowser browser, CefErrorCode error, string url, CefSslInfo info, CefCallback callback) => false;

        protected override bool GetAuthCredentials(CefBrowser browser, string originUrl, bool proxy, string host, int port, string realm, string scheme, CefAuthCallback callback) => false;

        protected override CefResourceRequestHandler GetResourceRequestHandler(CefBrowser browser, CefFrame frame, CefRequest request, bool navigation, bool download, string initiator, ref bool disableDefaultHandling) => _resources;

        protected override void OnRenderProcessTerminated(CefBrowser browser, CefTerminationStatus status)
        {
            lock (owner._gate)
            {
                if (owner._closing)
                {
                    return;
                }

                owner._snapshot = owner._snapshot with { State = UiWebViewState.Faulted, Loading = false, FailureCode = "cef-renderer-terminated" };
                owner._commands.Clear();
                owner.Changed();
            }
        }
    }

    private sealed class Resources : CefResourceRequestHandler
    {
        protected override CefCookieAccessFilter GetCookieAccessFilter(CefBrowser browser, CefFrame frame, CefRequest request) => null!;

        protected override CefReturnValue OnBeforeResourceLoad(CefBrowser browser, CefFrame frame, CefRequest request, CefCallback callback) => Allowed(request.Url) ? CefReturnValue.Continue : CefReturnValue.Cancel;

        protected override void OnResourceRedirect(CefBrowser browser, CefFrame frame, CefRequest request, CefResponse response, ref string url)
        {
            if (!Allowed(url))
            {
                url = "about:blank";
            }
        }

        protected override void OnProtocolExecution(CefBrowser browser, CefFrame frame, CefRequest request, ref bool allowOsExecution) => allowOsExecution = false;
    }

    private sealed class Permissions : CefPermissionHandler
    {
        protected override bool OnRequestMediaAccessPermission(CefBrowser browser, CefFrame frame, string origin, CefMediaAccessPermissionTypes permissions, CefMediaAccessCallback callback)
        {
            callback.Cancel();
            return true;
        }

        protected override bool OnShowPermissionPrompt(CefBrowser browser, ulong id, string origin, CefPermissionRequestTypes permissions, CefPermissionPromptCallback callback)
        {
            callback.Continue(CefPermissionRequestResult.Deny);
            return true;
        }
    }

    private sealed class Downloads : CefDownloadHandler
    {
        protected override bool CanDownload(CefBrowser browser, string url, string method) => false;

        // Not continuing this callback cancels the download; CanDownload also rejects it.
        protected override void OnBeforeDownload(CefBrowser browser, CefDownloadItem item, string suggestedName, CefBeforeDownloadCallback callback) => callback.Dispose();

        protected override void OnDownloadUpdated(CefBrowser browser, CefDownloadItem item, CefDownloadItemCallback callback) => callback.Cancel();
    }

    private sealed class Dialogs : CefDialogHandler
    {
        protected override bool OnFileDialog(CefBrowser browser, CefFileDialogMode mode, string title, string defaultPath, string[] filters, CefFileDialogCallback callback)
        {
            callback.Cancel();
            return true;
        }
    }

    private sealed class Menus : CefContextMenuHandler
    {
        protected override void OnBeforeContextMenu(CefBrowser browser, CefFrame frame, CefContextMenuParams parameters, CefMenuModel model) => model.Clear();
    }

    private sealed class JsDialogs : CefJSDialogHandler
    {
        protected override void OnResetDialogState(CefBrowser browser)
        {
        }

        protected override void OnDialogClosed(CefBrowser browser)
        {
        }

        protected override bool OnJSDialog(CefBrowser browser, string origin, CefJSDialogType type, string message, string prompt, CefJSDialogCallback callback, out bool suppress)
        {
            suppress = true;
            return false;
        }

        protected override bool OnBeforeUnloadDialog(CefBrowser browser, string message, bool reload, CefJSDialogCallback callback)
        {
            callback.Continue(true, string.Empty);
            return true;
        }
    }

    private sealed class Printing : CefPrintHandler
    {
        protected override void OnPrintReset(CefBrowser browser)
        {
        }

        protected override void OnPrintSettings(CefBrowser browser, CefPrintSettings settings, bool defaults)
        {
        }

        protected override bool OnPrintDialog(CefBrowser browser, bool selectionOnly, CefPrintDialogCallback callback)
        {
            callback.Cancel();
            return true;
        }

        protected override bool OnPrintJob(CefBrowser browser, string name, string path, CefPrintJobCallback callback) => false;

        protected override CefSize GetPdfPaperSize(CefBrowser browser, int unitsPerInch) => new(0, 0);
    }
}
