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

    [JSImport("createBuffer", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateBuffer(JSObject device, double size, int usage, int memory);

    [JSImport("mapBuffer", "Lumyte.Graphics.Browser")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task MapBufferAsync(JSObject buffer, int memory);

    [JSImport("unmapBuffer", "Lumyte.Graphics.Browser")]
    internal static partial void UnmapBuffer(JSObject buffer);

    [JSImport("copyBufferFrom", "Lumyte.Graphics.Browser")]
    internal static partial void CopyBufferFrom(JSObject buffer, [JSMarshalAs<JSType.MemoryView>] Span<byte> source, int offset);

    [JSImport("copyBufferTo", "Lumyte.Graphics.Browser")]
    internal static partial void CopyBufferTo(JSObject buffer, [JSMarshalAs<JSType.MemoryView>] Span<byte> destination, int offset);

    [JSImport("destroyBuffer", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyBuffer(JSObject buffer);

    [JSImport("createTexture", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateTexture(JSObject device, int width, int height, int layers, int mips, int format, int usage);

    [JSImport("createTextureView", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateTextureView(JSObject texture, int dimension, int baseMip, int mipCount, int baseLayer, int layerCount);

    [JSImport("destroyTexture", "Lumyte.Graphics.Browser")]
    internal static partial void DestroyTexture(JSObject texture);

    [JSImport("createShader", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateShader(JSObject device, string code);

    [JSImport("createSampler", "Lumyte.Graphics.Browser")]
    internal static partial JSObject CreateSampler(JSObject device, string descJson);
}
