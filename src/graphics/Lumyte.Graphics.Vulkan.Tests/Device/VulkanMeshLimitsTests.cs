using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe class VulkanMeshLimitsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshLimitsPreserveIndependentNativeDispatchBounds(bool amplification)
    {
        PhysicalDeviceMeshShaderPropertiesEXT properties = new()
        {
            MaxMeshWorkGroupTotalCount = uint.MaxValue, MaxTaskWorkGroupTotalCount = 4194304,
            MaxMeshOutputVertices = 256, MaxMeshOutputPrimitives = 512, MaxTaskPayloadSize = 16384,
        };
        properties.MaxMeshWorkGroupCount[0] = 65535;
        properties.MaxMeshWorkGroupCount[1] = 32768;
        properties.MaxMeshWorkGroupCount[2] = 16384;
        properties.MaxTaskWorkGroupCount[0] = 1024;
        properties.MaxTaskWorkGroupCount[1] = 2048;
        properties.MaxTaskWorkGroupCount[2] = 4096;

        NativeGpuMeshShaderLimits limits = VulkanBackend.MeshLimits(properties, amplification);

        Assert.Equal(new NativeGpuMeshShaderLimits(new(65535, 32768, 16384, uint.MaxValue),
            amplification ? new(1024, 2048, 4096, 4194304) : null, 256, 512, amplification ? 16384u : 0u), limits);
    }

    [Fact]
    public void MeshExtensionIsOptionalForTheNativeBaseline()
    {
        HashSet<string> extensions = ["VK_EXT_descriptor_heap", "VK_KHR_device_address_commands", "VK_KHR_shader_untyped_pointers"];

        string[] missing = VulkanBackend.MissingRequirements(VulkanBackend.RequiredApiVersion, extensions);

        Assert.Empty(missing);
    }

    [Fact]
    public void MeshAndTaskStagesPreserveTheirOwnBarrierScopes()
    {
        Assert.Equal(PipelineStageFlags2.MeshShaderBitExt | PipelineStageFlags2.TaskShaderBitExt,
            VulkanBackend.CommandStages(GpuStage.MeshShader | GpuStage.AmplificationShader));
    }
}
