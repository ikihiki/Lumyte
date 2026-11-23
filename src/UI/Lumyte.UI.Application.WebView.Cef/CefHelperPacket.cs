namespace Lumyte.UI.Application.WebView.Cef;

internal readonly record struct CefHelperPacket(uint Kind, long Generation = 0, long Sequence = 0, int Width = 0, int Height = 0, byte[]? Body = null);
