namespace Lumyte.UI.WebView.Cef;

internal readonly record struct CefEngineKey(bool Up, int WindowsKeyCode, int NativeKeyCode, CefEngineModifiers Modifiers, bool IsSystemKey = false);
