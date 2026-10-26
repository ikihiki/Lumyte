using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Platform.Browser;

internal static partial class BrowserInterop
{
    [JSImport("attach", "Lumyte.Platform.Browser")]
    internal static partial JSObject Attach(JSObject canvas, string title, double width, double height, bool preserveFocus);

    [JSImport("context", "Lumyte.Platform.Browser")]
    internal static partial JSObject Context(JSObject handle);

    [JSImport("snapshot", "Lumyte.Platform.Browser")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial double[] Snapshot(JSObject handle);

    [JSImport("drain", "Lumyte.Platform.Browser")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial double[] Drain(JSObject handle);

    [JSImport("show", "Lumyte.Platform.Browser")]
    internal static partial void Show(JSObject handle, bool visible);

    [JSImport("resize", "Lumyte.Platform.Browser")]
    internal static partial void Resize(JSObject handle, double width, double height);

    [JSImport("title", "Lumyte.Platform.Browser")]
    internal static partial void Title(JSObject handle, string title);

    [JSImport("activate", "Lumyte.Platform.Browser")]
    internal static partial bool Activate(JSObject handle);

    [JSImport("detach", "Lumyte.Platform.Browser")]
    internal static partial void Detach(JSObject handle);

    [JSImport("hasClipboard", "Lumyte.Platform.Browser")]
    internal static partial bool HasClipboard();

    [JSImport("readClipboard", "Lumyte.Platform.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> ReadClipboardAsync();

    [JSImport("writeClipboard", "Lumyte.Platform.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task WriteClipboardAsync(string text);

    [JSImport("drainInput", "Lumyte.Platform.Browser")]
    internal static partial string DrainInput(JSObject handle);
}
