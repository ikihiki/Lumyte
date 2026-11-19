namespace Lumyte.UI.WebView;

internal readonly record struct WebViewCommand(WebViewCommandKind Kind, Uri? Uri, long Sequence);
