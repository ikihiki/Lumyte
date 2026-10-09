using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanComputeEncoder(VulkanCommandBuffer owner) : IComputeEncoder
{
    public void End() => owner.EndCompute(this);
}
