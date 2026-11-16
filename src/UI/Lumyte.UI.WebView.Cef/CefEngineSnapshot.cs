namespace Lumyte.UI.WebView.Cef;

// Latest-only owner snapshot. Popup coordinates are logical viewport coordinates.
internal readonly record struct CefEngineSnapshot(UiWebViewState State, Uri? Uri, bool Loading, bool Back, bool Forward, string? FailureCode, long Sequence, long Generation, bool PopupVisible, int PopupX, int PopupY, int PopupWidth, int PopupHeight, bool? TakeFocusForward, long FocusRequest);
