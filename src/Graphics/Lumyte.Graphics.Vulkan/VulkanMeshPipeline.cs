using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanMeshPipeline : IMeshPipeline, IGraphicsNativeOrigin
{
    internal VulkanMeshPipeline(VulkanDevice owner, MeshPipelineDesc desc)
    {
        Desc = desc;
        Program = new(owner, desc);
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Program.Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => ((IGraphicsNativeOrigin)Program).AllocationIdentity;

    public MeshPipelineDesc Desc { get; }

    internal VulkanGraphicsPipeline Program { get; }

    void IGraphicsNativeOrigin.ValidateOrigin() => ((IGraphicsNativeOrigin)Program).ValidateOrigin();

    public void Dispose() => Program.Dispose();
}
