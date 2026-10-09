using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanRenderEncoder(VulkanCommandBuffer owner) : IRenderEncoder
{
    public void End() => owner.EndRender(this);
}
