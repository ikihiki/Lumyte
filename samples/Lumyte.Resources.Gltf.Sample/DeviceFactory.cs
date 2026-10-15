using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class DeviceFactory : IGraphicsDeviceFactory
{
    public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IGraphicsDeviceOwner>(new Owner(VulkanDevice.Create()));
    }

    private sealed class Owner(VulkanDevice device) : IGraphicsDeviceOwner
    {
        public IGraphicDevice Device => device;

        public ValueTask DisposeAsync()
        {
            device.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
