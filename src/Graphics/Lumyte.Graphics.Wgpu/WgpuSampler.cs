using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuSampler : IGraphicsSampler
{
    private readonly WgpuDevice _owner;
    private readonly WGPUSamplerImpl* _native;
    private bool _disposed;

    internal WgpuSampler(WgpuDevice owner, SamplerDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        var descriptor = new WGPUSamplerDescriptor
        {
            addressModeU = NativeAddress(desc.AddressU),
            addressModeV = NativeAddress(desc.AddressV),
            addressModeW = NativeAddress(desc.AddressW),
            minFilter = desc.MinFilter == FilterMode.Linear ? WGPUFilterMode.Linear : WGPUFilterMode.Nearest,
            magFilter = desc.MagFilter == FilterMode.Linear ? WGPUFilterMode.Linear : WGPUFilterMode.Nearest,
            mipmapFilter = desc.MipmapFilter == FilterMode.Linear ? WGPUMipmapFilterMode.Linear : WGPUMipmapFilterMode.Nearest,
            lodMinClamp = desc.LodMinClamp,
            lodMaxClamp = desc.LodMaxClamp,
            maxAnisotropy = desc.MaxAnisotropy,
            compare = desc.Compare is { } compare ? NativeCompare(compare) : WGPUCompareFunction.Undefined,
        };
        _native = WGPU.wgpuDeviceCreateSampler(owner.NativeDevice.Handle, &descriptor);
        if (_native == null)
        {
            throw new InvalidOperationException("WebGPU sampler creation failed.");
        }
    }

    public SamplerDesc Desc { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WGPU.wgpuSamplerRelease(_native);
        _disposed = true;
        _owner.ReleaseSampler();
    }

    private static WGPUAddressMode NativeAddress(AddressMode mode) => mode switch
    {
        AddressMode.ClampToEdge => WGPUAddressMode.ClampToEdge,
        AddressMode.Repeat => WGPUAddressMode.Repeat,
        AddressMode.MirrorRepeat => WGPUAddressMode.MirrorRepeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static WGPUCompareFunction NativeCompare(CompareFunction compare) => compare switch
    {
        CompareFunction.Never => WGPUCompareFunction.Never,
        CompareFunction.Less => WGPUCompareFunction.Less,
        CompareFunction.Equal => WGPUCompareFunction.Equal,
        CompareFunction.LessOrEqual => WGPUCompareFunction.LessEqual,
        CompareFunction.Greater => WGPUCompareFunction.Greater,
        CompareFunction.NotEqual => WGPUCompareFunction.NotEqual,
        CompareFunction.GreaterOrEqual => WGPUCompareFunction.GreaterEqual,
        CompareFunction.Always => WGPUCompareFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare)),
    };
}
