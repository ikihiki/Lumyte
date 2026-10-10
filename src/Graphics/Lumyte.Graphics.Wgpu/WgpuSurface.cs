using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSurface : IGraphicsSurface
{
    private readonly WgpuDevice _owner;
    private readonly A.Surface _native;
    private readonly A.Adapter _adapter;
    private WgpuSwapchain? _swapchain;
    private bool _disposed;

    internal WgpuSurface(WgpuDevice owner, A.Surface native, A.Adapter adapter)
    {
        (_owner, _native, _adapter) = (owner, native, adapter);
        _ = GetCapabilities();
        owner.RetainSurface();
    }

    internal A.Surface Native => _native;

    internal WgpuDevice Owner => _owner;

    public SurfaceCapabilities GetCapabilities()
    {
        ValidateAlive();
        A.SurfaceCapabilities caps = _native.GetCapabilities(_adapter);
        TextureFormat[] formats = Enum.GetValues<TextureFormat>().Where(f => f is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8) && caps.Formats.Contains(WgpuTexture.NativeFormat(f))).OrderBy(f => Array.IndexOf(caps.Formats, WgpuTexture.NativeFormat(f))).ToArray();
        if (formats.Length == 0)
        {
            throw new NotSupportedException("The selected adapter cannot present a supported format to this surface.");
        }

        TextureUsage usage = 0;
        if ((caps.Usages & A.TextureUsage.RenderAttachment) != 0)
        {
            usage |= TextureUsage.RenderAttachment;
        }

        if ((caps.Usages & A.TextureUsage.CopySrc) != 0)
        {
            usage |= TextureUsage.CopySource;
        }

        if ((caps.Usages & A.TextureUsage.CopyDst) != 0)
        {
            usage |= TextureUsage.CopyDestination;
        }

        if ((caps.Usages & A.TextureUsage.TextureBinding) != 0)
        {
            usage |= TextureUsage.Sampled;
        }

        var alpha = new List<SurfaceAlphaMode> { SurfaceAlphaMode.Auto };
        if (caps.AlphaModes.Contains(WGPUCompositeAlphaMode.Opaque))
        {
            alpha.Add(SurfaceAlphaMode.Opaque);
        }

        if (caps.AlphaModes.Contains(WGPUCompositeAlphaMode.Premultiplied))
        {
            alpha.Add(SurfaceAlphaMode.Premultiplied);
        }

        return new()
        {
            Formats = Array.AsReadOnly(formats),
            PresentModes = Array.AsReadOnly(Enum.GetValues<PresentMode>().Where(p => caps.PresentModes.Contains(Mode(p))).ToArray()),
            AlphaModes = alpha.AsReadOnly(),
            SupportedUsage = usage,
            MaxWidth = _owner.Caps.MaxTextureDimension2D,
            MaxHeight = _owner.Caps.MaxTextureDimension2D,
        };
    }

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        ValidateAlive();
        if (_swapchain != null)
        {
            throw new InvalidOperationException("The surface already owns a swapchain.");
        }

        _swapchain = new(this, desc);
        return _swapchain;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_swapchain != null)
        {
            throw new InvalidOperationException("Dispose the swapchain before its surface.");
        }

        _native.Dispose();
        _disposed = true;
        _owner.ReleaseSurface();
    }

    internal void Configure(SwapchainDesc desc)
    {
        SurfaceValidation.Validate(desc, GetCapabilities());
        A.TextureUsage usage = A.TextureUsage.RenderAttachment;
        if ((desc.Usage & TextureUsage.CopySource) != 0)
        {
            usage |= A.TextureUsage.CopySrc;
        }

        if ((desc.Usage & TextureUsage.CopyDestination) != 0)
        {
            usage |= A.TextureUsage.CopyDst;
        }

        if ((desc.Usage & TextureUsage.Sampled) != 0)
        {
            usage |= A.TextureUsage.TextureBinding;
        }

        WGPUCompositeAlphaMode alpha = desc.AlphaMode switch
        {
            SurfaceAlphaMode.Opaque => WGPUCompositeAlphaMode.Opaque,
            SurfaceAlphaMode.Premultiplied => WGPUCompositeAlphaMode.Premultiplied,
            _ => WGPUCompositeAlphaMode.Auto,
        };
        _native.Configure(_owner.NativeDevice, WgpuTexture.NativeFormat(desc.Format), usage, desc.Width, desc.Height, Mode(desc.PresentMode), alpha);
    }

    internal void ReleaseSwapchain() => _swapchain = null;

    internal void ValidateAlive()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static WGPUPresentMode Mode(PresentMode mode) => mode switch
    {
        PresentMode.Fifo => WGPUPresentMode.Fifo,
        PresentMode.Mailbox => WGPUPresentMode.Mailbox,
        _ => WGPUPresentMode.Immediate,
    };
}
