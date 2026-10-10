using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuTexture : IGraphicsTexture
{
    private readonly WgpuDevice _owner;
    private readonly A.Texture _native;
    private int _viewCount;
    private bool _disposed;

    internal WgpuTexture(WgpuDevice owner, TextureDesc desc)
    {
        (_owner, Width, Height, MipLevels, ArrayLayers, Format, Usage) = (owner, desc.Width, desc.Height, desc.MipLevels, desc.ArrayLayers, desc.Format, desc.Usage);
        A.TextureUsage usage = 0;
        if ((Usage & TextureUsage.CopySource) != 0)
        {
            usage |= A.TextureUsage.CopySrc;
        }

        if ((Usage & TextureUsage.CopyDestination) != 0)
        {
            usage |= A.TextureUsage.CopyDst;
        }

        if ((Usage & TextureUsage.Sampled) != 0)
        {
            usage |= A.TextureUsage.TextureBinding;
        }

        if ((Usage & TextureUsage.RenderAttachment) != 0)
        {
            usage |= A.TextureUsage.RenderAttachment;
        }

        WGPUTextureFormat format = Format switch
        {
            TextureFormat.Rgba8Unorm => WGPUTextureFormat.RGBA8Unorm,
            TextureFormat.Rgba8Srgb => WGPUTextureFormat.RGBA8UnormSrgb,
            TextureFormat.Bgra8Unorm => WGPUTextureFormat.BGRA8Unorm,
            TextureFormat.Bgra8Srgb => WGPUTextureFormat.BGRA8UnormSrgb,
            TextureFormat.R8Unorm => WGPUTextureFormat.R8Unorm,
            TextureFormat.Rg8Unorm => WGPUTextureFormat.RG8Unorm,
            TextureFormat.R16Float => WGPUTextureFormat.R16Float,
            TextureFormat.Rg16Float => WGPUTextureFormat.RG16Float,
            TextureFormat.Rgba16Float => WGPUTextureFormat.RGBA16Float,
            TextureFormat.Rgb10A2Unorm => WGPUTextureFormat.RGB10A2Unorm,
            TextureFormat.Depth32Float => WGPUTextureFormat.Depth32Float,
            TextureFormat.Depth24Stencil8 => WGPUTextureFormat.Depth24PlusStencil8,
            _ => throw new NotSupportedException("Unsupported texture format."),
        };
        _native = owner.NativeDevice.CreateTexture(new A.TextureDescriptor
        {
            Size = new WGPUExtent3D { width = Width, height = Height, depthOrArrayLayers = ArrayLayers },
            Dimension = WGPUTextureDimension._2D,
            Format = format,
            Usage = usage,
            MipLevelCount = MipLevels,
            SampleCount = 1,
        });
    }

    public uint Width { get; }

    public uint Height { get; }

    public uint MipLevels { get; }

    public uint ArrayLayers { get; }

    public TextureFormat Format { get; }

    public TextureUsage Usage { get; }

    internal A.Texture Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _native;
        }
    }

    internal WgpuDevice Owner => _owner;

    public (uint Width, uint Height) GetMipSize(uint mipLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }

        return (Math.Max(1U, Width >> (int)mipLevel), Math.Max(1U, Height >> (int)mipLevel));
    }

    public IGraphicsTextureView CreateView(TextureViewDesc? desc = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureViewInfo info = TextureValidation.Resolve(this, desc);
        WGPUTextureViewDimension dimension = info.Dimension switch
        {
            TextureViewDimension.D2 => WGPUTextureViewDimension._2D,
            TextureViewDimension.D2Array => WGPUTextureViewDimension._2DArray,
            TextureViewDimension.Cube => WGPUTextureViewDimension.Cube,
            TextureViewDimension.CubeArray => WGPUTextureViewDimension.CubeArray,
            _ => throw new NotSupportedException("Unsupported view dimension."),
        };
        A.TextureView native = _native.CreateView(new A.TextureViewDescriptor
        {
            Dimension = dimension,
            Aspect = WGPUTextureAspect.All,
            BaseMipLevel = info.BaseMipLevel,
            MipLevelCount = info.MipLevelCount,
            BaseArrayLayer = info.BaseArrayLayer,
            ArrayLayerCount = info.ArrayLayerCount,
        });
        var view = new WgpuTextureView(this, info, native);
        _viewCount++;
        return view;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_viewCount != 0)
        {
            throw new InvalidOperationException("Dispose all views before disposing their texture.");
        }

        _native.Dispose();
        _disposed = true;
        _owner.ReleaseTexture();
    }

    internal void ReleaseView() => _viewCount--;
}
