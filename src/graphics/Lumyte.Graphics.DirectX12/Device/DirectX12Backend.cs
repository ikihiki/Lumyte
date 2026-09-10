using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

/// <summary>Owns the Direct3D 12 device used by the Native API.</summary>
public sealed unsafe partial class DirectX12Backend : INativeGpuBackend
{
    private readonly D3D12 api;
    private ComPtr<ID3D12Device> device;
    private ComPtr<ID3D12Device10> device10;
    private NativeQueue mainQueue = null!;
    private string? deviceLoss;
    private bool disposed;

    private DirectX12Backend(D3D12 api, ComPtr<ID3D12Device> device, ComPtr<ID3D12Device10> device10)
    {
        this.api = api;
        this.device = device;
        this.device10 = device10;
    }

    public static DirectX12Backend Create(NativeGpuBackendOptions? options = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 is available only on Windows.");
        }

        D3D12 api = D3D12.GetApi();
        ComPtr<ID3D12Device> device = default;
        ComPtr<ID3D12Device10> device10 = default;
        try
        {
            if (options?.EnableValidation == true)
            {
                ComPtr<ID3D12Debug> debug = default;
                try
                {
                    Check(api.GetDebugInterface(out debug), "D3D12GetDebugInterface");
                    debug.EnableDebugLayer();
                }
                finally
                {
                    debug.Dispose();
                }
            }

            Check(api.CreateDevice<IDXGIAdapter, ID3D12Device>(
                default, D3DFeatureLevel.Level110, out device), "D3D12CreateDevice");
            Check(device.QueryInterface(out device10), "QueryInterface(ID3D12Device10)");
            RequireNativeFeatures(device);
            var backend = new DirectX12Backend(api, device, device10);
            backend.mainQueue = backend.CreateMainQueue();
            return backend;
        }
        catch
        {
            device10.Dispose();
            device.Dispose();
            api.Dispose();
            throw;
        }
    }

    public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.Dxil;
    public NativeGpuCapabilities Capabilities => new(ExplicitTextureTransitions: true);
    public NativeGpuQueue MainQueue => mainQueue;

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        mainQueue.DisposeNativeObjects();
        device10.Dispose();
        device.Dispose();
        api.Dispose();
    }

    private void VerifyNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private void VerifyAvailable()
    {
        VerifyNotDisposed();
        if (deviceLoss is not null) { throw new GpuDeviceLostException(deviceLoss); }
    }

    private static void RequireNativeFeatures(ComPtr<ID3D12Device> device)
    {
        var options = new FeatureDataD3D12Options12();
        Check(device.CheckFeatureSupport(Silk.NET.Direct3D12.Feature.D3D12Options12, &options,
            (uint)sizeof(FeatureDataD3D12Options12)), "CheckFeatureSupport(D3D12_OPTIONS12)");
        if (!options.EnhancedBarriersSupported)
        {
            throw new NotSupportedException("The Native Direct3D 12 backend requires enhanced barriers.");
        }

        var shaderModel = new FeatureDataShaderModel { HighestShaderModel = D3DShaderModel.ShaderModel66 };
        Check(device.CheckFeatureSupport(Silk.NET.Direct3D12.Feature.ShaderModel, &shaderModel,
            (uint)sizeof(FeatureDataShaderModel)), "CheckFeatureSupport(SHADER_MODEL 6.6)");
        if (shaderModel.HighestShaderModel < D3DShaderModel.ShaderModel66)
        {
            throw new NotSupportedException("The Native Direct3D 12 backend requires Shader Model 6.6.");
        }
    }

    private static void Check(int result, string operation)
    {
        if (result >= 0) { return; }
        if (result is unchecked((int)0x887A0005) or unchecked((int)0x887A0006)
            or unchecked((int)0x887A0007))
        {
            throw new GpuDeviceLostException($"{operation} failed with HRESULT 0x{result:X8}.");
        }
        throw new NativeGpuException($"{operation} failed with HRESULT 0x{result:X8}.", result);
    }
}
