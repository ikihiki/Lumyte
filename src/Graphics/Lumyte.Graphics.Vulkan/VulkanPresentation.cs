using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanPresentation(KhrSurface surface, KhrSwapchain swapchain, ExtSwapchainMaintenance1 maintenance) : IDisposable
{
    internal KhrSurface Surface => surface;

    internal KhrSwapchain Swapchain => swapchain;

    internal ExtSwapchainMaintenance1 Maintenance => maintenance;

    public void Dispose()
    {
        maintenance.Dispose();
        swapchain.Dispose();
        surface.Dispose();
    }

    internal static SurfaceStatus Status(Result result) => result switch
    {
        Result.Success => SurfaceStatus.Success,
        Result.SuboptimalKhr => SurfaceStatus.Suboptimal,
        Result.Timeout or Result.NotReady => SurfaceStatus.Timeout,
        Result.ErrorOutOfDateKhr => SurfaceStatus.Outdated,
        Result.ErrorSurfaceLostKhr => SurfaceStatus.Lost,
        Result.ErrorDeviceLost => SurfaceStatus.DeviceLost,
        _ => throw new InvalidOperationException($"Vulkan presentation operation failed: {result}."),
    };

    internal static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }
}
