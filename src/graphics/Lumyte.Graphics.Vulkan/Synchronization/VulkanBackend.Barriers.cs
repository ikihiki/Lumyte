using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    internal static PipelineStageFlags2 CommandStages(GpuStage value)
    {
        const GpuStage known = GpuStage.DrawIndirect | GpuStage.VertexShader | GpuStage.PixelShader
            | GpuStage.ComputeShader | GpuStage.ColorOutput | GpuStage.DepthStencil | GpuStage.Copy
            | GpuStage.AllGraphics | GpuStage.All | GpuStage.IndexInput | GpuStage.AmplificationShader
            | GpuStage.MeshShader | GpuStage.Host;
        if ((value & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        PipelineStageFlags2 result = 0;
        if ((value & GpuStage.DrawIndirect) != 0) { result |= PipelineStageFlags2.DrawIndirectBit; }
        if ((value & GpuStage.VertexShader) != 0) { result |= PipelineStageFlags2.VertexShaderBit; }
        if ((value & GpuStage.PixelShader) != 0) { result |= PipelineStageFlags2.FragmentShaderBit; }
        if ((value & GpuStage.ComputeShader) != 0) { result |= PipelineStageFlags2.ComputeShaderBit; }
        if ((value & GpuStage.ColorOutput) != 0) { result |= PipelineStageFlags2.ColorAttachmentOutputBit; }
        if ((value & GpuStage.DepthStencil) != 0) { result |= PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit; }
        if ((value & GpuStage.Copy) != 0) { result |= PipelineStageFlags2.CopyBit; }
        if ((value & GpuStage.AllGraphics) != 0) { result |= PipelineStageFlags2.AllGraphicsBit; }
        if ((value & GpuStage.All) != 0) { result |= PipelineStageFlags2.AllCommandsBit; }
        if ((value & GpuStage.IndexInput) != 0) { result |= PipelineStageFlags2.IndexInputBit; }
        if ((value & GpuStage.AmplificationShader) != 0) { result |= PipelineStageFlags2.TaskShaderBitExt; }
        if ((value & GpuStage.MeshShader) != 0) { result |= PipelineStageFlags2.MeshShaderBitExt; }
        if ((value & GpuStage.Host) != 0) { result |= PipelineStageFlags2.HostBit; }
        return result;
    }

    internal static AccessFlags2 CommandAccess(GpuAccess value)
    {
        const GpuAccess known = GpuAccess.ShaderRead | GpuAccess.ShaderWrite | GpuAccess.DescriptorRead
            | GpuAccess.ColorRead | GpuAccess.ColorWrite | GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite
            | GpuAccess.CopyRead | GpuAccess.CopyWrite | GpuAccess.IndexRead | GpuAccess.IndirectRead
            | GpuAccess.HostRead | GpuAccess.HostWrite;
        if ((value & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
        AccessFlags2 result = 0;
        if ((value & GpuAccess.ShaderRead) != 0) { result |= AccessFlags2.ShaderReadBit; }
        if ((value & GpuAccess.ShaderWrite) != 0) { result |= AccessFlags2.ShaderWriteBit; }
        // VK_ACCESS_2_RESOURCE_HEAP_READ_BIT_EXT and VK_ACCESS_2_SAMPLER_HEAP_READ_BIT_EXT.
        if ((value & GpuAccess.DescriptorRead) != 0) { result |= (AccessFlags2)0x600000000000000UL; }
        if ((value & GpuAccess.ColorRead) != 0) { result |= AccessFlags2.ColorAttachmentReadBit; }
        if ((value & GpuAccess.ColorWrite) != 0) { result |= AccessFlags2.ColorAttachmentWriteBit; }
        if ((value & GpuAccess.DepthStencilRead) != 0) { result |= AccessFlags2.DepthStencilAttachmentReadBit; }
        if ((value & GpuAccess.DepthStencilWrite) != 0) { result |= AccessFlags2.DepthStencilAttachmentWriteBit; }
        if ((value & GpuAccess.CopyRead) != 0) { result |= AccessFlags2.TransferReadBit; }
        if ((value & GpuAccess.CopyWrite) != 0) { result |= AccessFlags2.TransferWriteBit; }
        if ((value & GpuAccess.IndexRead) != 0) { result |= AccessFlags2.IndexReadBit; }
        if ((value & GpuAccess.IndirectRead) != 0) { result |= AccessFlags2.IndirectCommandReadBit; }
        if ((value & GpuAccess.HostRead) != 0) { result |= AccessFlags2.HostReadBit; }
        if ((value & GpuAccess.HostWrite) != 0) { result |= AccessFlags2.HostWriteBit; }
        return result;
    }

    private void RecordDiscard(CommandBuffer command, TextureRecord texture, ImageSubresourceRange range)
    {
        ImageMemoryBarrier2 barrier = new()
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.AllCommandsBit,
            DstStageMask = PipelineStageFlags2.AllCommandsBit,
            DstAccessMask = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
            OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.General,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = texture.Image, SubresourceRange = range,
        };
        DependencyInfo dependency = new()
        {
            SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &barrier,
        };
        vk.CmdPipelineBarrier2(command, &dependency);
    }
}
