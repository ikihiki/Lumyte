namespace Lumyte.Platform.Browser;

internal sealed record BrowserInputEvent(int Type, string? Code, bool Down, bool Repeat, double X, double Y, ulong? Capture);
