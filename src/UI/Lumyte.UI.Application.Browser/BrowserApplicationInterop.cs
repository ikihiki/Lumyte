using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.UI.Application.Browser;

internal static partial class BrowserApplicationInterop
{
    [JSImport("canvas", "Lumyte.UI.Application")]
    internal static partial JSObject Canvas(string id);

    [JSImport("context", "Lumyte.UI.Application")]
    internal static partial JSObject Context(JSObject canvas);

    [JSImport("visible", "Lumyte.UI.Application")]
    internal static partial bool Visible(JSObject canvas);

    [JSImport("connected", "Lumyte.UI.Application")]
    internal static partial bool Connected(JSObject canvas);

    [JSImport("nextTurn", "Lumyte.UI.Application")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task NextTurnAsync(double milliseconds);
}
