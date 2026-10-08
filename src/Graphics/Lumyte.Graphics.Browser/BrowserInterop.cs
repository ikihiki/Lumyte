using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Graphics.Browser;

internal static partial class BrowserInterop
{
    [JSImport("createDevice", "Lumyte.Graphics.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>]
    internal static partial Task<JSObject> CreateDeviceAsync();

    [JSImport("getCapsJson", "Lumyte.Graphics.Browser")]
    internal static partial string GetCapsJson(JSObject handle);

    [JSImport("destroyDevice", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyDevice(JSObject handle);
}
