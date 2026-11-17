using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Resolves enabled Mesh/Task limits and synchronization stages.</summary>
public sealed unsafe partial class VulkanDevice
{
    internal ShaderStageFlags GraphicsShaderStages => VulkanPushConstants.GraphicsStages |
        ((Caps.Features & GraphicsFeatures.MeshShader) != 0 ? ShaderStageFlags.MeshBitExt : 0) |
        ((Caps.Features & GraphicsFeatures.TaskShader) != 0 ? ShaderStageFlags.TaskBitExt : 0);

    internal PipelineStage NormalizeStages(PipelineStage stages) => MeshShaderValidation.NormalizeStages(stages, Caps.Features);

    private static DeviceCaps WithMeshCaps(DeviceCaps caps, PhysicalDeviceMeshShaderPropertiesEXT p, bool mesh, bool task)
    {
        return caps with
        {
            Features = caps.Features | (mesh ? GraphicsFeatures.MeshShader : 0) | (task ? GraphicsFeatures.TaskShader : 0),
            MeshShaderLimits = !mesh ? null : new()
            {
                Workgroup = new()
                {
                    MaxSizeX = p.MaxMeshWorkGroupSize[0],
                    MaxSizeY = p.MaxMeshWorkGroupSize[1],
                    MaxSizeZ = p.MaxMeshWorkGroupSize[2],
                    MaxInvocations = p.MaxMeshWorkGroupInvocations,
                    MaxGroupCountX = p.MaxMeshWorkGroupCount[0],
                    MaxGroupCountY = p.MaxMeshWorkGroupCount[1],
                    MaxGroupCountZ = p.MaxMeshWorkGroupCount[2],
                    MaxTotalGroupCount = p.MaxMeshWorkGroupTotalCount,
                },
                MaxOutputVertices = p.MaxMeshOutputVertices,
                MaxOutputPrimitives = p.MaxMeshOutputPrimitives,
                MaxSharedMemoryBytes = p.MaxMeshSharedMemorySize,
                MaxPayloadBytes = p.MaxTaskPayloadSize,
                MaxPayloadAndSharedMemoryBytes = p.MaxMeshPayloadAndSharedMemorySize,
                MaxOutputMemoryBytes = p.MaxMeshOutputMemorySize,
                MaxPayloadAndOutputMemoryBytes = p.MaxMeshPayloadAndOutputMemorySize,
            },
            TaskShaderLimits = !task ? null : new()
            {
                Workgroup = new()
                {
                    MaxSizeX = p.MaxTaskWorkGroupSize[0],
                    MaxSizeY = p.MaxTaskWorkGroupSize[1],
                    MaxSizeZ = p.MaxTaskWorkGroupSize[2],
                    MaxInvocations = p.MaxTaskWorkGroupInvocations,
                    MaxGroupCountX = p.MaxTaskWorkGroupCount[0],
                    MaxGroupCountY = p.MaxTaskWorkGroupCount[1],
                    MaxGroupCountZ = p.MaxTaskWorkGroupCount[2],
                    MaxTotalGroupCount = p.MaxTaskWorkGroupTotalCount,
                },
                MaxSharedMemoryBytes = p.MaxTaskSharedMemorySize,
                MaxPayloadBytes = p.MaxTaskPayloadSize,
                MaxPayloadAndSharedMemoryBytes = p.MaxTaskPayloadAndSharedMemorySize,
            },
        };
    }
}
