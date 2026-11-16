namespace Lumyte.UI.WebView.Cef;

internal readonly record struct CefEnginePointer(CefEnginePointerKind Kind, int X, int Y, CefEngineModifiers Modifiers, CefEngineMouseButton Button = CefEngineMouseButton.Left, int ClickCount = 1, int DeltaX = 0, int DeltaY = 0);
