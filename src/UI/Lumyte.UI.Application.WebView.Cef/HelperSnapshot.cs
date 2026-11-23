using Lumyte.UI.WebView;

namespace Lumyte.UI.Application.WebView.Cef;

internal readonly record struct HelperSnapshot(UiWebViewState State, Uri? Uri = null, bool Loading = false, bool Back = false, bool Forward = false, string? FailureCode = null, long Sequence = 0);
