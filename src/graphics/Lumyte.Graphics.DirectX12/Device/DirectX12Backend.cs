using Lumyte.Graphics.Native;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

/// <summary>Owns the Direct3D 12 device used by the Native API.</summary>
/// <remarks>
/// The Native compute ABI uses root parameter zero as up to 64 constants at b0, space0,
/// with both directly-indexed heap flags. The caller selects both resource and sampler
/// descriptor heaps before compute work, including when its shader does not access them.
/// </remarks>
public sealed unsafe partial class DirectX12Backend : INativeGpuBackend
{
    private readonly D3D12 api;
    private ComPtr<ID3D12Device> device;
    private ComPtr<ID3D12Device10> device10;
    private ComPtr<ID3D12RootSignature> computeRootSignature;
    private ComPtr<ID3D12CommandSignature> dispatchSignature;
    private ComPtr<ID3D12CommandSignature> drawSignature;
    private ComPtr<ID3D12CommandSignature> drawIndexedSignature;
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
        DirectX12Backend? backend = null;
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
            backend = new DirectX12Backend(api, device, device10);
            backend.CreateComputeSupport();
            backend.mainQueue = backend.CreateMainQueue();
            return backend;
        }
        catch
        {
            backend?.DisposeComputeSupport();
            device10.Dispose();
            device.Dispose();
            api.Dispose();
            throw;
        }
    }

    public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.Dxil;
    public NativeGpuCapabilities Capabilities => new(BufferDescriptors: true, ExplicitTextureTransitions: true);
    public NativeGpuLimits Limits => new(256, new(65535, 65535, 65535, 65535ul * 65535 * 65535));
    public NativeGpuQueue MainQueue => mainQueue;

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        mainQueue.DisposeNativeObjects();
        DisposeComputeSupport();
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
        var stencil = new FeatureDataD3D12Options14();
        Check(device.CheckFeatureSupport(Silk.NET.Direct3D12.Feature.D3D12Options14, &stencil,
            (uint)sizeof(FeatureDataD3D12Options14)), "CheckFeatureSupport(D3D12_OPTIONS14)");
        if (!stencil.IndependentFrontAndBackStencilRefMaskSupported)
        {
            throw new NotSupportedException("The Native Direct3D 12 backend requires independent front/back stencil references.");
        }
        var renderPasses = new FeatureDataD3D12Options18();
        Check(device.CheckFeatureSupport(Silk.NET.Direct3D12.Feature.D3D12Options18, &renderPasses,
            (uint)sizeof(FeatureDataD3D12Options18)), "CheckFeatureSupport(D3D12_OPTIONS18)");
        if (!renderPasses.RenderPassesValid)
        {
            throw new NotSupportedException("The Native Direct3D 12 backend requires the corrected render-pass runtime.");
        }

        var binding = new FeatureDataD3D12Options();
        Check(device.CheckFeatureSupport(Silk.NET.Direct3D12.Feature.D3D12Options, &binding,
            (uint)sizeof(FeatureDataD3D12Options)), "CheckFeatureSupport(D3D12_OPTIONS)");
        if (binding.ResourceBindingTier < ResourceBindingTier.Tier3)
        {
            throw new NotSupportedException("The Native Direct3D 12 backend requires Resource Binding Tier 3.");
        }

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
