using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Texture : IGraphicsTexture
{
    private nint _native;

    internal Direct3D12Texture(Direct3D12Device owner, TextureDesc desc)
    {
        Owner = owner;
        (Width, Height, MipLevels, ArrayLayers, Format, Usage) = (desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        owner.Check(NativeMethods.TextureCreate(owner.Native, Width, Height, MipLevels, ArrayLayers, NativeFormat(Format), (uint)Usage, out _native));
    }

    internal Direct3D12Texture(Direct3D12Device owner, TextureDesc desc, nint native, SurfaceFrameLifetime frame)
    {
        Owner = owner;
        (Width, Height, MipLevels, ArrayLayers, Format, Usage) = (desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        _native = native;
        SurfaceFrame = frame;
    }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public TextureFormat Format { get; }

    public TextureUsage Usage { get; }

    internal Direct3D12Device Owner { get; }

    internal SurfaceFrameLifetime? SurfaceFrame { get; }

    internal nint Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_native == 0, this);
            if (SurfaceFrame != null && SurfaceFrame.Status != SurfaceFrameStatus.Acquired)
            {
                throw new InvalidOperationException("The surface image lease is no longer acquired.");
            }

            Owner.RequireReady();
            return _native;
        }
    }

    public (uint Width, uint Height) GetMipSize(uint level)
    {
        _ = Native;
        if (level >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        return (Math.Max(1, Width >> (int)level), Math.Max(1, Height >> (int)level));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        _ = Native;
        return new Direct3D12TextureView(this, TextureValidation.Resolve(this, desc));
    }

    public void Dispose()
    {
        if (SurfaceFrame != null)
        {
            throw new InvalidOperationException("The frame owns this image lease.");
        }

        DisposeLease();
    }

    internal static uint NativeFormat(TextureFormat format) => format switch
    {
        TextureFormat.Rgba8Unorm => 28,
        TextureFormat.Rgba8Srgb => 29,
        TextureFormat.Bgra8Unorm => 87,
        TextureFormat.Bgra8Srgb => 91,
        TextureFormat.R8Unorm => 61,
        TextureFormat.Rg8Unorm => 49,
        TextureFormat.R16Float => 54,
        TextureFormat.Rg16Float => 34,
        TextureFormat.Rgba16Float => 10,
        TextureFormat.Rgb10A2Unorm => 24,
        TextureFormat.Depth32Float => 40,
        TextureFormat.Depth24Stencil8 => 45,
        _ => throw new NotSupportedException("Unsupported texture format."),
    };

    internal void DisposeLease()
    {
        NativeMethods.Release(_native);
        _native = 0;
    }
}
