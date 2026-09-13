using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Owns an independent Portable WebGPU device supplied by the packaged Dawn runtime.</summary>
public sealed partial class WebGpuBackend : P.IPortableGpuBackend
{
    // Dawn's native extension is not included in WebGPUSharp's standard-only feature enum.
    // dawn.json: feature "implicit device synchronization", Dawn extension namespace + value 4.
    private const N.FeatureName ImplicitDeviceSynchronization = (N.FeatureName)0x00050004;
    private readonly object gate = new();
    private readonly WebGpuDeviceStatus status = new();
    private F.InstanceHandle instance;
    private F.AdapterHandle adapter;
    private F.DeviceHandle device;
    private WebGpuEventDriver? events;
    private bool disposed;

    private WebGpuBackend() { }

    public P.GpuBackendCapabilities Capabilities { get; private set; }
    public P.GpuDeviceLimits Limits { get; private set; } = new();

    /// <summary>Requests the required direct input support and a device with the selected effective limits.</summary>
    public static async ValueTask<WebGpuBackend> CreateAsync(P.GpuBackendOptions? options = null)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.RequiredLimits);
        var result = new WebGpuBackend();
        Task<(N.RequestAdapterStatus, F.AdapterHandle, string)>? adapterRequest = null;
        Task<(N.RequestDeviceStatus, F.DeviceHandle, string)>? deviceRequest = null;
        bool adapterClaimed = false;
        bool deviceClaimed = false;
        try
        {
            result.InitializeInstance();
            adapterRequest = result.RequestAdapter();
            var adapterResult = await result.AwaitCallbackAsync(adapterRequest).ConfigureAwait(false);
            result.adapter = adapterResult.Item2;
            adapterClaimed = true;
            if (adapterResult.Item1 != N.RequestAdapterStatus.Success || (nuint)result.adapter == 0)
            { throw new NotSupportedException($"WebGPU adapter request failed ({adapterResult.Item1}): {adapterResult.Item3}"); }
            deviceRequest = result.RequestDevice(options);
            var deviceResult = await result.AwaitCallbackAsync(deviceRequest).ConfigureAwait(false);
            result.device = deviceResult.Item2;
            deviceClaimed = true;
            if (deviceResult.Item1 != N.RequestDeviceStatus.Success || (nuint)result.device == 0)
            { throw new P.GpuOperationException("RequestDevice", [new(P.GpuDiagnosticKind.Runtime,
                $"WebGPU device request failed ({deviceResult.Item1}): {deviceResult.Item3}")]); }
            result.ReadEffectiveConfiguration();
            result.status.ThrowIfFailed();
            return result;
        }
        catch
        {
            result.Dispose();
            if (!deviceClaimed && deviceRequest != null) { _ = ReleaseUnclaimedDeviceAsync(deviceRequest); }
            if (!adapterClaimed && adapterRequest != null) { _ = ReleaseUnclaimedAdapterAsync(adapterRequest); }
            throw;
        }
    }

    private static async Task ReleaseUnclaimedAdapterAsync(Task<(N.RequestAdapterStatus, F.AdapterHandle, string)> request)
    {
        try
        {
            var result = await request.ConfigureAwait(false);
            if ((nuint)result.Item2 != 0) { F.WebGPU_FFI.AdapterRelease(result.Item2); }
        }
        catch { /* Initialization has already failed; the callback still owns its userdata until completion. */ }
    }

    private static async Task ReleaseUnclaimedDeviceAsync(Task<(N.RequestDeviceStatus, F.DeviceHandle, string)> request)
    {
        try
        {
            var result = await request.ConfigureAwait(false);
            if ((nuint)result.Item2 != 0)
            {
                F.WebGPU_FFI.DeviceDestroy(result.Item2);
                F.WebGPU_FFI.DeviceRelease(result.Item2);
            }
        }
        catch { /* Preserve the original initialization failure. */ }
    }

    private async Task<T> AwaitCallbackAsync<T>(Task<T> callback)
    {
        await Task.WhenAny(callback, status.Failure).ConfigureAwait(false);
        if (!callback.IsCompleted) { status.ThrowIfFailed(); }
        return await callback.ConfigureAwait(false);
    }

    private unsafe void InitializeInstance()
    {
        N.InstanceFeatureName feature = N.InstanceFeatureName.TimedWaitAny;
        var description = new F.InstanceDescriptorFFI { RequiredFeatureCount = 1, RequiredFeatures = &feature };
        instance = F.WebGPU_FFI.CreateInstance(&description);
        if ((nuint)instance == 0) { throw new NotSupportedException("WebGPU instance creation failed."); }
        if (!F.WebGPU_FFI.InstanceHasWGSLLanguageFeature(instance, N.WGSLLanguageFeatureName.ImmediateAddressSpace))
        { throw new NotSupportedException("WebGPU requires WGSL immediate_address_space. Buffer fallback is disabled."); }
        events = new(instance, status);
    }

    private unsafe Task<(N.RequestAdapterStatus, F.AdapterHandle, string)> RequestAdapter()
    {
        var result = new WebGpuCallbacks.Result<(N.RequestAdapterStatus, F.AdapterHandle, string)>();
        var description = new F.RequestAdapterOptionsFFI { PowerPreference = N.PowerPreference.HighPerformance };
        bool submitted = false;
        try
        {
            N.Future future = F.WebGPU_FFI.InstanceRequestAdapter(instance, &description, new()
            {
                Mode = N.CallbackMode.AllowSpontaneous,
                Callback = &WebGpuCallbacks.Adapter,
                Userdata1 = result.Allocate(),
            });
            submitted = true;
            events!.Register(future, result.Task);
        }
        catch
        {
            if (!submitted) { result.Release(); }
            else { _ = ReleaseUnclaimedAdapterAsync(result.Task); }
            throw;
        }
        return result.Task;
    }

    private unsafe Task<(N.RequestDeviceStatus, F.DeviceHandle, string)> RequestDevice(P.GpuBackendOptions options)
    {
        N.Limits supported = new();
        if (F.WebGPU_FFI.AdapterGetLimits(adapter, &supported) != N.Status.Success)
        { throw new NotSupportedException("WebGPU adapter limits are unavailable."); }
        N.Limits required = MapRequiredLimits(options.RequiredLimits, supported.MaxImmediateSize);
        if (required.MaxImmediateSize == 0)
        { throw new NotSupportedException("WebGPU requires a nonzero immediate input limit. Buffer fallback is disabled."); }
        N.FeatureName* features = stackalloc N.FeatureName[3];
        // The instance event driver may wait concurrently with host-side resource operations.
        features[0] = ImplicitDeviceSynchronization;
        nuint featureCount = 1;
        if (options.RequireDualSourceBlend) { features[featureCount++] = N.FeatureName.DualSourceBlending; }
        if (options.RequireIndirectFirstInstance) { features[featureCount++] = N.FeatureName.IndirectFirstInstance; }
        nint owner = status.RegisterCallbacks();
        var description = new F.DeviceDescriptorFFI
        {
            RequiredLimits = &required,
            RequiredFeatures = features,
            RequiredFeatureCount = featureCount,
            DeviceLostCallbackInfo = new()
            {
                Mode = N.CallbackMode.AllowSpontaneous,
                Callback = &WebGpuCallbacks.Lost,
                Userdata1 = (void*)owner,
            },
            UncapturedErrorCallbackInfo = new()
            {
                Callback = &WebGpuCallbacks.Uncaptured,
                Userdata1 = (void*)owner,
            },
        };
        var result = new WebGpuCallbacks.Result<(N.RequestDeviceStatus, F.DeviceHandle, string)>();
        bool submitted = false;
        try
        {
            N.Future future = F.WebGPU_FFI.AdapterRequestDevice(adapter, &description, new()
            {
                Mode = N.CallbackMode.AllowSpontaneous,
                Callback = &WebGpuCallbacks.Device,
                Userdata1 = result.Allocate(),
            });
            submitted = true;
            events!.Register(future, result.Task);
        }
        catch
        {
            if (!submitted) { result.Release(); status.ReleaseCallbacks(); }
            else { _ = ReleaseUnclaimedDeviceAsync(result.Task); }
            throw;
        }
        return result.Task;
    }

    private unsafe void ReadEffectiveConfiguration()
    {
        N.Limits limits = new();
        if (F.WebGPU_FFI.DeviceGetLimits(device, &limits) != N.Status.Success)
        { throw new NotSupportedException("WebGPU device limits are unavailable."); }
        if (limits.MaxImmediateSize == 0)
        { throw new NotSupportedException("WebGPU device did not enable direct immediate inputs."); }
        Limits = MapEffectiveLimits(limits);
        Capabilities = new(true,
            F.WebGPU_FFI.DeviceHasFeature(device, N.FeatureName.DualSourceBlending),
            F.WebGPU_FFI.DeviceHasFeature(device, N.FeatureName.IndirectFirstInstance));
    }

    private void RequireAvailable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        status.ThrowIfFailed();
    }

    private void PushScopes()
    {
        try
        {
            F.WebGPU_FFI.DevicePushErrorScope(device, N.ErrorFilter.Validation);
            F.WebGPU_FFI.DevicePushErrorScope(device, N.ErrorFilter.OutOfMemory);
            F.WebGPU_FFI.DevicePushErrorScope(device, N.ErrorFilter.Internal);
        }
        catch (Exception error)
        {
            status.Lose($"WebGPU diagnostic scope setup failed: {error.Message}");
            throw;
        }
    }

    private unsafe Task<IReadOnlyList<P.GpuDiagnostic>> PopScopes()
    {
        Task<IReadOnlyList<P.GpuDiagnostic>>[] scopes = new Task<IReadOnlyList<P.GpuDiagnostic>>[3];
        for (int index = 0; index < scopes.Length; index++)
        {
            var result = new WebGpuCallbacks.Result<IReadOnlyList<P.GpuDiagnostic>>();
            bool submitted = false;
            try
            {
                N.Future future = F.WebGPU_FFI.DevicePopErrorScope(device, new()
                {
                    Mode = N.CallbackMode.AllowSpontaneous,
                    Callback = &WebGpuCallbacks.Scope,
                    Userdata1 = result.Allocate(),
                });
                submitted = true;
                events!.Register(future, result.Task);
            }
            catch (Exception error)
            {
                if (!submitted) { result.Release(); }
                status.Lose($"WebGPU diagnostic scope completion failed: {error.Message}");
                throw;
            }
            scopes[index] = result.Task;
        }
        return WebGpuDiagnostics.CombineAsync(scopes);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            status.Lose("WebGPU backend was disposed.");
            events?.Dispose();
            events = null;
            // The caller ends resource use before disposal. No resource registry or implicit GPU wait is needed.
            if ((nuint)device != 0)
            {
                F.WebGPU_FFI.DeviceDestroy(device);
                F.WebGPU_FFI.DeviceRelease(device);
                device = default;
            }
            if ((nuint)adapter != 0) { F.WebGPU_FFI.AdapterRelease(adapter); adapter = default; }
            // Cancellation callbacks own and release their userdata, including a failed device request's lost event.
            if ((nuint)instance != 0) { F.WebGPU_FFI.InstanceRelease(instance); instance = default; }
        }
    }
}
