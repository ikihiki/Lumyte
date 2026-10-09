using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSampler : IGraphicsSampler
{
    private readonly VulkanDevice _owner;
    private readonly Sampler _native;
    private bool _disposed;

    internal VulkanSampler(VulkanDevice owner, SamplerDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        var descriptor = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MinFilter = desc.MinFilter == FilterMode.Linear ? Filter.Linear : Filter.Nearest,
            MagFilter = desc.MagFilter == FilterMode.Linear ? Filter.Linear : Filter.Nearest,
            MipmapMode = desc.MipmapFilter == FilterMode.Linear ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest,
            AddressModeU = NativeAddress(desc.AddressU),
            AddressModeV = NativeAddress(desc.AddressV),
            AddressModeW = NativeAddress(desc.AddressW),
            MinLod = desc.LodMinClamp,
            MaxLod = desc.LodMaxClamp,
            AnisotropyEnable = desc.MaxAnisotropy > 1,
            MaxAnisotropy = desc.MaxAnisotropy,
            CompareEnable = desc.Compare.HasValue,
            CompareOp = desc.Compare is { } compare ? NativeCompare(compare) : CompareOp.Never,
            UnnormalizedCoordinates = false,
        };
        Sampler native = default;
        Result result = owner.Api.CreateSampler(owner.NativeDevice, &descriptor, null, &native);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan CreateSampler failed: {result}.");
        }

        _native = native;
    }

    public SamplerDesc Desc { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Api.DestroySampler(_owner.NativeDevice, _native, null);
        _disposed = true;
        _owner.ReleaseSampler();
    }

    private static SamplerAddressMode NativeAddress(AddressMode mode) => mode switch
    {
        AddressMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
        AddressMode.Repeat => SamplerAddressMode.Repeat,
        AddressMode.MirrorRepeat => SamplerAddressMode.MirroredRepeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static CompareOp NativeCompare(CompareFunction compare) => compare switch
    {
        CompareFunction.Never => CompareOp.Never,
        CompareFunction.Less => CompareOp.Less,
        CompareFunction.Equal => CompareOp.Equal,
        CompareFunction.LessOrEqual => CompareOp.LessOrEqual,
        CompareFunction.Greater => CompareOp.Greater,
        CompareFunction.NotEqual => CompareOp.NotEqual,
        CompareFunction.GreaterOrEqual => CompareOp.GreaterOrEqual,
        CompareFunction.Always => CompareOp.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare)),
    };
}
