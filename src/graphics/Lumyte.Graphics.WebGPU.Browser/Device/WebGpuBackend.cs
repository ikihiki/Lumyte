using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

/// <summary>An independent Portable device backed by the browser's WebGPU API.</summary>
/// <remarks>The runtime is borrowed. Use and dispose the backend on its runtime's creating thread.</remarks>
[SupportedOSPlatform("browser")]
public sealed partial class WebGpuBackend : P.IPortableGpuBackend
{
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };
    private readonly WebGpuBrowserRuntime runtime;
    private readonly JSObject device;
    private readonly BrowserDeviceStatus status = new();
    private bool disposed;

    private WebGpuBackend(WebGpuBrowserRuntime runtime, JSObject device)
    {
        this.runtime = runtime;
        this.device = device;
    }

    public P.GpuBackendCapabilities Capabilities { get; private set; }
    public P.GpuDeviceLimits Limits { get; private set; } = new();

    /// <summary>Creates and owns a device with direct root inputs, borrowing the supplied runtime.</summary>
    public static async ValueTask<WebGpuBackend> CreateAsync(WebGpuBrowserRuntime runtime, P.GpuBackendOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.RequireAvailable();
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.RequiredLimits);
        if (options.RequiredLimits.MaxBufferSize is ulong maxBuffer) { Exact(maxBuffer, nameof(options.RequiredLimits.MaxBufferSize)); }
        if (options.RequiredLimits.MaxUniformBufferBindingSize is ulong uniform) { Exact(uniform, nameof(options.RequiredLimits.MaxUniformBufferBindingSize)); }
        if (options.RequiredLimits.MaxStorageBufferBindingSize is ulong storage) { Exact(storage, nameof(options.RequiredLimits.MaxStorageBufferBindingSize)); }
        JSObject device;
        try { device = await BrowserInterop.RequestDeviceAsync(Json(options)); }
        catch (JSException error) when (error.Message.Contains("LUMYTE_NOT_SUPPORTED:", StringComparison.Ordinal))
        { throw new NotSupportedException(error.Message, error); }
        catch (JSException error)
        { throw new P.GpuOperationException("RequestDevice", [new(P.GpuDiagnosticKind.Runtime, error.Message)]); }
        var result = new WebGpuBackend(runtime, device);
        try
        {
            runtime.RequireAvailable();
            _ = result.ObserveDeviceFailureAsync(BrowserInterop.ObserveFailureAsync(device));
            DeviceConfiguration configuration = JsonSerializer.Deserialize<DeviceConfiguration>(BrowserInterop.GetConfiguration(device), jsonOptions)
                ?? throw new InvalidOperationException("The browser returned no device configuration.");
            result.Capabilities = configuration.Capabilities;
            result.Limits = configuration.Limits;
            result.InitializeQueue();
            result.status.ThrowIfFailed();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private sealed record DeviceConfiguration(P.GpuBackendCapabilities Capabilities, P.GpuDeviceLimits Limits);

    private async Task ObserveDeviceFailureAsync(Task<string> observation)
    {
        try { status.Lose(await observation.ConfigureAwait(false)); }
        catch (Exception error) { status.Lose($"Browser WebGPU device observation failed: {error.Message}"); }
    }

    private void RequireAvailable()
    {
        runtime.RequireAvailable();
        ObjectDisposedException.ThrowIf(disposed, this);
        status.ThrowIfFailed();
    }

    private (JSObject Handle, Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics) CreateObject(
        JSObject owner, string kind, object descriptor, JSObject[] references)
    {
        RequireAvailable();
        string description = Json(descriptor);
        BrowserInterop.PushErrorScopes(device);
        JSObject? handle = null;
        try
        {
            handle = BrowserInterop.Create(owner, kind, description, references);
        }
        catch (Exception error)
        {
            _ = ReadDiagnosticsAsync(BrowserInterop.PopErrorScopesAsync(device));
            if (error is JSException)
            { throw new P.GpuOperationException(kind, [new(P.GpuDiagnosticKind.Runtime, error.Message)]); }
            throw;
        }
        try { return (handle, ReadDiagnosticsAsync(BrowserInterop.PopErrorScopesAsync(device))); }
        catch
        {
            handle.Dispose();
            status.Lose("Browser WebGPU diagnostic scope completion failed.");
            throw;
        }
    }

    private async Task<IReadOnlyList<P.GpuDiagnostic>> ReadDiagnosticsAsync(Task<string> observation)
    {
        try
        {
            string json = await observation.ConfigureAwait(false);
            return JsonSerializer.Deserialize<P.GpuDiagnostic[]>(json, jsonOptions)
                ?? throw new InvalidOperationException("The browser returned no diagnostic result.");
        }
        catch (Exception error)
        {
            status.Lose($"Browser WebGPU diagnostic observation failed: {error.Message}");
            throw;
        }
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, jsonOptions);
    private static object Ref(int index) => new Dictionary<string, int> { ["$ref"] = index };
    private static double Exact(ulong value, string name)
    {
        if (value > 9_007_199_254_740_991)
        { throw new ArgumentOutOfRangeException(name, "The value cannot be represented by WebGPU's JavaScript integer input."); }
        return value;
    }

    public void Dispose()
    {
        runtime.RequireThread();
        if (disposed) { return; }
        disposed = true;
        status.Lose("Browser WebGPU backend was disposed.");
        // A failed interop call does not establish that device.destroy() reached the runtime.
        // Retain uncertain command storage instead of releasing it from a finally block.
        BrowserDeviceDisposal.Destroy(
            () => BrowserInterop.Destroy(device),
            () => { StopQueue(); device.Dispose(); });
    }
}
