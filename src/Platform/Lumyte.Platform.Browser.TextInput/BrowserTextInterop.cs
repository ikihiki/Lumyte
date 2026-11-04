using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Platform.Browser.TextInput;

internal static partial class BrowserTextInterop
{
    [JSImport("setInputGate", "Lumyte.Platform.Browser.TextInput")]

    internal static partial void SetInputGate(JSObject handle, bool gated);

    [JSImport("create", "Lumyte.Platform.Browser.TextInput")]
    internal static partial JSObject Create(JSObject canvas);

    [JSImport("proxy", "Lumyte.Platform.Browser.TextInput")]

    internal static partial JSObject Proxy(JSObject handle);

    [JSImport("begin", "Lumyte.Platform.Browser.TextInput")]
    internal static partial bool Begin(JSObject handle, string session, string generation, string value, int anchor, int active);

    [JSImport("drain", "Lumyte.Platform.Browser.TextInput")]

    internal static partial string Drain(JSObject handle);

    [JSImport("synchronize", "Lumyte.Platform.Browser.TextInput")]
    internal static partial void Synchronize(JSObject handle, string value, int anchor, int active);

    [JSImport("requestEnd", "Lumyte.Platform.Browser.TextInput")]

    internal static partial void RequestEnd(JSObject handle);

    [JSImport("endReady", "Lumyte.Platform.Browser.TextInput")]
    internal static partial bool EndReady(JSObject handle);

    [JSImport("pending", "Lumyte.Platform.Browser.TextInput")]

    internal static partial bool Pending(JSObject handle);

    [JSImport("composing", "Lumyte.Platform.Browser.TextInput")]
    internal static partial bool Composing(JSObject handle);

    [JSImport("geometry", "Lumyte.Platform.Browser.TextInput")]
    internal static partial void Geometry(JSObject handle, double x, double y, double width, double height);

    [JSImport("release", "Lumyte.Platform.Browser.TextInput")]
    internal static partial void Release(JSObject handle);

    [JSImport("destroy", "Lumyte.Platform.Browser.TextInput")]
    internal static partial void Destroy(JSObject handle);

    [JSImport("quiescent", "Lumyte.Platform.Browser.TextInput")]
    internal static partial bool Quiescent(JSObject handle);
}
