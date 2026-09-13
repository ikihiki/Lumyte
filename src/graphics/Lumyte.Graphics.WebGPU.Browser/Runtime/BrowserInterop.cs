using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Lumyte.Graphics.WebGPU.Browser;

[SupportedOSPlatform("browser")]
internal static partial class BrowserInterop
{
    [JSImport("requestDevice", WebGpuBrowserRuntime.ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.Object>>()]
    internal static partial Task<JSObject> RequestDeviceAsync(string optionsJson);

    [JSImport("getConfiguration", WebGpuBrowserRuntime.ModuleName)]
    internal static partial string GetConfiguration(JSObject device);

    [JSImport("observeFailure", WebGpuBrowserRuntime.ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>()]
    internal static partial Task<string> ObserveFailureAsync(JSObject device);

    [JSImport("pushErrorScopes", WebGpuBrowserRuntime.ModuleName)]
    internal static partial void PushErrorScopes(JSObject device);

    [JSImport("popErrorScopes", WebGpuBrowserRuntime.ModuleName)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>()]
    internal static partial Task<string> PopErrorScopesAsync(JSObject device);

    [JSImport("create", WebGpuBrowserRuntime.ModuleName)]
    internal static partial JSObject Create(JSObject owner, string kind, string descriptorJson,
        [JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] references);

    [JSImport("encode", WebGpuBrowserRuntime.ModuleName)]
    internal static partial JSObject Encode(JSObject device, string commandsJson,
        [JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] references);

    [JSImport("submit", WebGpuBrowserRuntime.ModuleName)]
    internal static partial void Submit(JSObject device,
        [JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] buffers);

    [JSImport("onSubmittedWorkDone", WebGpuBrowserRuntime.ModuleName)]
    internal static partial Task OnSubmittedWorkDoneAsync(JSObject device);

    [JSImport("map", WebGpuBrowserRuntime.ModuleName)]
    internal static partial Task MapAsync(JSObject buffer, int mode, double offset, double length);

    [JSImport("getMappedRange", WebGpuBrowserRuntime.ModuleName)]
    internal static partial JSObject GetMappedRange(JSObject buffer, double offset, double length);

    [JSImport("readMapped", WebGpuBrowserRuntime.ModuleName)]
    internal static partial void ReadMapped(JSObject mappedRange,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> destination);

    [JSImport("writeMapped", WebGpuBrowserRuntime.ModuleName)]
    private static partial void WriteMappedCore(JSObject mappedRange,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> source);

    internal static void WriteMapped(JSObject mappedRange, ReadOnlySpan<byte> source)
    {
        // The imported function only copies from this span synchronously; it never stores or writes the view.
        Span<byte> view = MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(source)), source.Length);
        WriteMappedCore(mappedRange, view);
    }

    [JSImport("unmap", WebGpuBrowserRuntime.ModuleName)]
    internal static partial void Unmap(JSObject buffer);

    [JSImport("destroy", WebGpuBrowserRuntime.ModuleName)]
    internal static partial void Destroy(JSObject resource);
}
