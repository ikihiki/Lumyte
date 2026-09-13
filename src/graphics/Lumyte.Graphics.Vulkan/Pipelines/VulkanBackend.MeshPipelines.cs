using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private delegate* unmanaged<CommandBuffer, uint, uint, uint, void> drawMeshTasks;
    private delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void> drawMeshTasksIndirect;

    private void InitializeMesh(PhysicalDevice physicalDevice)
    {
        if (!supportsMeshShaders) { return; }
        drawMeshTasks = (delegate* unmanaged<CommandBuffer, uint, uint, uint, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdDrawMeshTasksEXT");
        drawMeshTasksIndirect = (delegate* unmanaged<CommandBuffer, NativeDrawIndirectInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdDrawMeshTasksIndirect2EXT");
        if (drawMeshTasks == null || drawMeshTasksIndirect == null)
        {
            throw new NotSupportedException("Enabled Native Vulkan mesh entry points are unavailable.");
        }
        PhysicalDeviceMeshShaderPropertiesEXT mesh = new() { SType = StructureType.PhysicalDeviceMeshShaderPropertiesExt };
        PhysicalDeviceProperties2 properties = new() { SType = StructureType.PhysicalDeviceProperties2, PNext = &mesh };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties);
        meshLimits = MeshLimits(mesh, supportsAmplificationShaders);
    }

    internal static NativeGpuMeshShaderLimits MeshLimits(PhysicalDeviceMeshShaderPropertiesEXT properties, bool amplification) => new(
        new(properties.MaxMeshWorkGroupCount[0], properties.MaxMeshWorkGroupCount[1], properties.MaxMeshWorkGroupCount[2], properties.MaxMeshWorkGroupTotalCount),
        amplification ? new NativeGpuDispatchLimits(properties.MaxTaskWorkGroupCount[0], properties.MaxTaskWorkGroupCount[1],
            properties.MaxTaskWorkGroupCount[2], properties.MaxTaskWorkGroupTotalCount) : null,
        properties.MaxMeshOutputVertices, properties.MaxMeshOutputPrimitives, amplification ? properties.MaxTaskPayloadSize : 0);
}
