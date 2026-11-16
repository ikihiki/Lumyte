using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanMeshPipeline : IMeshPipeline
{
    internal VulkanMeshPipeline(VulkanDevice owner, MeshPipelineDesc desc)
    {
        Desc = desc;
        Program = new(owner, desc);
    }

    public MeshPipelineDesc Desc { get; }

    internal VulkanGraphicsPipeline Program { get; }

    public void Dispose() => Program.Dispose();
}
