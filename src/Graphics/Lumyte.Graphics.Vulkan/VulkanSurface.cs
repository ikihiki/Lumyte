using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanSurface : IGraphicsSurface
{
    private readonly VulkanDevice _owner;
    private readonly SurfaceKHR _native;
    private bool _disposed;

    internal VulkanSurface(VulkanDevice owner, SurfaceKHR native)
    {
        (_owner, _native) = (owner, native);
        VulkanPresentation.Check(owner.Presentation.Surface.GetPhysicalDeviceSurfaceSupport(owner.PhysicalDevice, owner.QueueFamily, native, out Silk.NET.Core.Bool32 supported), "GetPhysicalDeviceSurfaceSupport");
        if (!supported)
        {
            throw new NotSupportedException("The device queue cannot present to this supplied surface.");
        }
    }

    internal VulkanDevice Owner => _owner;

    internal SurfaceKHR Native => _native;

    public SurfaceCapabilities GetCapabilities()
    {
        ValidateAlive();
        SurfaceCapabilitiesKHR caps = ReadNativeCapabilities();
        SurfaceFormatKHR[] nativeFormats = ReadFormats();
        PresentModeKHR[] nativeModes = ReadModes();
        TextureFormat[] formats = Enum.GetValues<TextureFormat>().Where(f => f is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8) && nativeFormats.Any(n => n.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr && (n.Format == VulkanTexture.NativeFormat(f) || n.Format == V.Format.Undefined))).OrderBy(f => Array.FindIndex(nativeFormats, n => n.Format == VulkanTexture.NativeFormat(f) || n.Format == V.Format.Undefined)).ToArray();
        if (formats.Length == 0)
        {
            throw new NotSupportedException("The surface has no supported nonlinear-sRGB presentation format.");
        }

        TextureUsage usage = 0;
        if ((caps.SupportedUsageFlags & ImageUsageFlags.ColorAttachmentBit) != 0)
        {
            usage |= TextureUsage.RenderAttachment;
        }

        if ((caps.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0)
        {
            usage |= TextureUsage.CopySource;
        }

        if ((caps.SupportedUsageFlags & ImageUsageFlags.TransferDstBit) != 0)
        {
            usage |= TextureUsage.CopyDestination;
        }

        if ((caps.SupportedUsageFlags & ImageUsageFlags.SampledBit) != 0)
        {
            usage |= TextureUsage.Sampled;
        }

        var alpha = new List<SurfaceAlphaMode> { SurfaceAlphaMode.Auto };
        if ((caps.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0)
        {
            alpha.Add(SurfaceAlphaMode.Opaque);
        }

        if ((caps.SupportedCompositeAlpha & CompositeAlphaFlagsKHR.PreMultipliedBitKhr) != 0)
        {
            alpha.Add(SurfaceAlphaMode.Premultiplied);
        }

        return new()
        {
            Formats = Array.AsReadOnly(formats),
            PresentModes = Array.AsReadOnly(Enum.GetValues<PresentMode>().Where(m => nativeModes.Contains(Mode(m))).ToArray()),
            AlphaModes = alpha.AsReadOnly(),
            SupportedUsage = usage,
            MinWidth = caps.MinImageExtent.Width,
            MinHeight = caps.MinImageExtent.Height,
            MaxWidth = Math.Min(caps.MaxImageExtent.Width, _owner.Caps.MaxTextureDimension2D),
            MaxHeight = Math.Min(caps.MaxImageExtent.Height, _owner.Caps.MaxTextureDimension2D),
            CurrentWidth = caps.CurrentExtent.Width == uint.MaxValue ? null : caps.CurrentExtent.Width,
            CurrentHeight = caps.CurrentExtent.Height == uint.MaxValue ? null : caps.CurrentExtent.Height,
        };
    }

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        ValidateAlive();
        return new VulkanSwapchain(this, desc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _owner.Presentation.Surface.DestroySurface(_owner.NativeInstance, _native, null);
        _disposed = true;
    }

    internal static PresentModeKHR Mode(PresentMode mode) => mode switch
    {
        PresentMode.Fifo => PresentModeKHR.FifoKhr,
        PresentMode.Mailbox => PresentModeKHR.MailboxKhr,
        _ => PresentModeKHR.ImmediateKhr,
    };

    internal SurfaceCapabilitiesKHR ReadNativeCapabilities()
    {
        VulkanPresentation.Check(_owner.Presentation.Surface.GetPhysicalDeviceSurfaceCapabilities(_owner.PhysicalDevice, _native, out SurfaceCapabilitiesKHR caps), "GetPhysicalDeviceSurfaceCapabilities");
        return caps;
    }

    internal void ValidateAlive()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private SurfaceFormatKHR[] ReadFormats()
    {
        while (true)
        {
            uint count = 0;
            VulkanPresentation.Check(_owner.Presentation.Surface.GetPhysicalDeviceSurfaceFormats(_owner.PhysicalDevice, _native, &count, null), "GetPhysicalDeviceSurfaceFormats");
            var formats = new SurfaceFormatKHR[count];
            fixed (SurfaceFormatKHR* pointer = formats)
            {
                Result result = _owner.Presentation.Surface.GetPhysicalDeviceSurfaceFormats(_owner.PhysicalDevice, _native, &count, pointer);
                if (result == Result.Incomplete)
                {
                    continue;
                }

                VulkanPresentation.Check(result, "GetPhysicalDeviceSurfaceFormats");
            }

            return formats.AsSpan(0, checked((int)count)).ToArray();
        }
    }

    private PresentModeKHR[] ReadModes()
    {
        while (true)
        {
            uint count = 0;
            VulkanPresentation.Check(_owner.Presentation.Surface.GetPhysicalDeviceSurfacePresentModes(_owner.PhysicalDevice, _native, &count, null), "GetPhysicalDeviceSurfacePresentModes");
            var modes = new PresentModeKHR[count];
            fixed (PresentModeKHR* pointer = modes)
            {
                Result result = _owner.Presentation.Surface.GetPhysicalDeviceSurfacePresentModes(_owner.PhysicalDevice, _native, &count, pointer);
                if (result == Result.Incomplete)
                {
                    continue;
                }

                VulkanPresentation.Check(result, "GetPhysicalDeviceSurfacePresentModes");
            }

            return modes.AsSpan(0, checked((int)count)).ToArray();
        }
    }
}
