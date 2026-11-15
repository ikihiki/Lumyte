using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationSurface : IGraphicsSurface
{
    public PresentationSwapchain Swapchain { get; } = new();

    public SurfaceCapabilities Capabilities { get; } = new()
    {
        Formats = [TextureFormat.Bgra8Unorm],
        PresentModes = [PresentMode.Fifo],
        AlphaModes = [SurfaceAlphaMode.Auto],
        SupportedUsage = TextureUsage.RenderAttachment,
        MaxWidth = 4096,
        MaxHeight = 4096,
    };

    public int DisposeCount { get; private set; }

    public int CreateCount { get; private set; }

    public SurfaceCapabilities GetCapabilities() => Capabilities;

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        CreateCount++;
        Swapchain.Reconfigure(desc);
        return Swapchain;
    }

    public void Dispose() => DisposeCount++;
}
