using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using VkCommandBuffer = Silk.NET.Vulkan.CommandBuffer;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanCommandBuffer
{
    private CommandPool _pool;
    private VkCommandBuffer _command;

    internal VkCommandBuffer Native => _command;

    internal void EndRender(VulkanRenderEncoder pass)
    {
        ValidateEnd(pass);
        _owner.Api.CmdEndRendering(_command);
        _active = null;
    }

    internal void EndCompute(VulkanComputeEncoder pass)
    {
        ValidateEnd(pass);
        _active = null;
    }

    private static PipelineStageFlags2 Stages(BarrierScope scope)
    {
        PipelineStageFlags flags = 0;
        if ((scope.Stages & PipelineStage.Host) != 0)
        {
            flags |= PipelineStageFlags.HostBit;
        }

        if ((scope.Stages & PipelineStage.Copy) != 0)
        {
            flags |= PipelineStageFlags.TransferBit;
        }

        if ((scope.Stages & PipelineStage.VertexShader) != 0)
        {
            flags |= PipelineStageFlags.VertexShaderBit;
        }

        if ((scope.Stages & PipelineStage.FragmentShader) != 0)
        {
            flags |= PipelineStageFlags.FragmentShaderBit;
        }

        if ((scope.Stages & PipelineStage.ComputeShader) != 0)
        {
            flags |= PipelineStageFlags.ComputeShaderBit;
        }

        if ((scope.Stages & PipelineStage.ColorOutput) != 0)
        {
            flags |= PipelineStageFlags.ColorAttachmentOutputBit;
        }

        if ((scope.Stages & PipelineStage.DrawIndirect) != 0)
        {
            flags |= PipelineStageFlags.DrawIndirectBit;
        }

        if ((scope.Stages & PipelineStage.IndexInput) != 0)
        {
            flags |= PipelineStageFlags.VertexInputBit;
        }

        if ((scope.Stages & PipelineStage.DepthStencil) != 0)
        {
            flags |= PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit;
        }

        return (PipelineStageFlags2)flags;
    }

    private static AccessFlags2 Access(BarrierScope scope)
    {
        AccessFlags flags = 0;
        if ((scope.Access & ResourceAccess.HostRead) != 0)
        {
            flags |= AccessFlags.HostReadBit;
        }

        if ((scope.Access & ResourceAccess.HostWrite) != 0)
        {
            flags |= AccessFlags.HostWriteBit;
        }

        if ((scope.Access & ResourceAccess.CopyRead) != 0)
        {
            flags |= AccessFlags.TransferReadBit;
        }

        if ((scope.Access & ResourceAccess.CopyWrite) != 0)
        {
            flags |= AccessFlags.TransferWriteBit;
        }

        if ((scope.Access & ResourceAccess.ShaderRead) != 0)
        {
            flags |= AccessFlags.ShaderReadBit;
        }

        if ((scope.Access & ResourceAccess.ShaderWrite) != 0)
        {
            flags |= AccessFlags.ShaderWriteBit;
        }

        if ((scope.Access & ResourceAccess.ColorRead) != 0)
        {
            flags |= AccessFlags.ColorAttachmentReadBit;
        }

        if ((scope.Access & ResourceAccess.ColorWrite) != 0)
        {
            flags |= AccessFlags.ColorAttachmentWriteBit;
        }

        if ((scope.Access & ResourceAccess.IndexRead) != 0)
        {
            flags |= AccessFlags.IndexReadBit;
        }

        if ((scope.Access & ResourceAccess.IndirectRead) != 0)
        {
            flags |= AccessFlags.IndirectCommandReadBit;
        }

        if ((scope.Access & ResourceAccess.DepthStencilRead) != 0)
        {
            flags |= AccessFlags.DepthStencilAttachmentReadBit;
        }

        if ((scope.Access & ResourceAccess.DepthStencilWrite) != 0)
        {
            flags |= AccessFlags.DepthStencilAttachmentWriteBit;
        }

        return (AccessFlags2)flags;
    }

    private static ImageLayout ImageState(TextureState state) => state switch
    {
        TextureState.Undefined => ImageLayout.Undefined,
        TextureState.CopySource => ImageLayout.TransferSrcOptimal,
        TextureState.CopyDestination => ImageLayout.TransferDstOptimal,
        TextureState.Sampled => ImageLayout.ShaderReadOnlyOptimal,
        TextureState.ColorAttachment => ImageLayout.ColorAttachmentOptimal,
        TextureState.DepthStencilAttachment => ImageLayout.DepthStencilAttachmentOptimal,
        _ => throw new ArgumentException("Unknown texture state."),
    };

    private static ImageSubresourceLayers Layers(TextureCopyRegion region) => new()
    {
        AspectMask = ImageAspectFlags.ColorBit,
        MipLevel = region.MipLevel,
        BaseArrayLayer = region.BaseArrayLayer,
        LayerCount = region.ArrayLayerCount,
    };

    private static Offset3D Origin(TextureCopyRegion region) => new(checked((int)region.OriginX), checked((int)region.OriginY), 0);

    private static Extent3D Extent(TextureCopyRegion region) => new(region.Width, region.Height, 1);

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }

    private void Initialize()
    {
        var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = _owner.QueueFamily, Flags = CommandPoolCreateFlags.TransientBit };
        Check(_owner.Api.CreateCommandPool(_owner.NativeDevice, &poolInfo, null, out _pool), "CreateCommandPool");
        try
        {
            var allocate = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
            Check(_owner.Api.AllocateCommandBuffers(_owner.NativeDevice, &allocate, out _command), "AllocateCommandBuffers");
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            Check(_owner.Api.BeginCommandBuffer(_command, &begin), "BeginCommandBuffer");
        }
        catch
        {
            _owner.Api.DestroyCommandPool(_owner.NativeDevice, _pool, null);
            throw;
        }
    }

    private void CopyBufferNative<TSource, TDestination>(VulkanBuffer<TSource> source, ulong sourceOffset, VulkanBuffer<TDestination> destination, ulong destinationOffset, ulong size)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var region = new BufferCopy { SrcOffset = sourceOffset, DstOffset = destinationOffset, Size = size };
        _owner.Api.CmdCopyBuffer(_command, source.Native, destination.Native, 1, &region);
    }

    private void CopyTextureNative(TextureCopyRegion source, TextureCopyRegion destination)
    {
        var copy = new ImageCopy { SrcSubresource = Layers(source), DstSubresource = Layers(destination), SrcOffset = Origin(source), DstOffset = Origin(destination), Extent = Extent(source) };
        _owner.Api.CmdCopyImage(_command, ((VulkanTexture)source.Texture).Native, ImageLayout.TransferSrcOptimal, ((VulkanTexture)destination.Texture).Native, ImageLayout.TransferDstOptimal, 1, &copy);
    }

    private void CopyBufferTextureNative(BufferTextureCopyLayout buffer, TextureCopyRegion region, bool upload)
    {
        var copy = new BufferImageCopy
        {
            BufferOffset = buffer.Buffer.OffsetInBytes,
            BufferRowLength = buffer.BytesPerRow / _owner.GetTextureCopyLayout(region.Texture.Format).BytesPerTexel,
            BufferImageHeight = buffer.RowsPerImage,
            ImageSubresource = Layers(region),
            ImageOffset = Origin(region),
            ImageExtent = Extent(region),
        };
        if (upload)
        {
            _owner.Api.CmdCopyBufferToImage(_command, ((VulkanBuffer<byte>)buffer.Buffer.Buffer).Native, ((VulkanTexture)region.Texture).Native, ImageLayout.TransferDstOptimal, 1, &copy);
        }
        else
        {
            _owner.Api.CmdCopyImageToBuffer(_command, ((VulkanTexture)region.Texture).Native, ImageLayout.TransferSrcOptimal, ((VulkanBuffer<byte>)buffer.Buffer.Buffer).Native, 1, &copy);
        }
    }

    private void MemoryBarrierNative(MemoryBarrierDesc barrier)
    {
        var memory = new MemoryBarrier2 { SType = StructureType.MemoryBarrier2, SrcStageMask = Stages(barrier.Before), SrcAccessMask = Access(barrier.Before), DstStageMask = Stages(barrier.After), DstAccessMask = Access(barrier.After) };
        var dependency = new DependencyInfo { SType = StructureType.DependencyInfo, MemoryBarrierCount = 1, PMemoryBarriers = &memory };
        _owner.Api.CmdPipelineBarrier2(_command, &dependency);
    }

    private void BufferBarrierNative<T>(VulkanBuffer<T> buffer, BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        var memory = new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = Stages(barrier.Before),
            SrcAccessMask = Access(barrier.Before),
            DstStageMask = Stages(barrier.After),
            DstAccessMask = Access(barrier.After),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Buffer = buffer.Native,
            Offset = barrier.Buffer.OffsetInBytes,
            Size = barrier.Buffer.SizeInBytes,
        };
        var dependency = new DependencyInfo { SType = StructureType.DependencyInfo, BufferMemoryBarrierCount = 1, PBufferMemoryBarriers = &memory };
        _owner.Api.CmdPipelineBarrier2(_command, &dependency);
    }

    private void TextureBarrierNative(VulkanTexture texture, TextureBarrierDesc barrier)
    {
        var memory = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = Stages(barrier.Before),
            SrcAccessMask = Access(barrier.Before),
            DstStageMask = Stages(barrier.After),
            DstAccessMask = Access(barrier.After),
            OldLayout = ImageState(barrier.BeforeState),
            NewLayout = ImageState(barrier.AfterState),
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = texture.Native,
            SubresourceRange = new() { AspectMask = VulkanTexture.Aspect(texture.Format), BaseMipLevel = barrier.Range.BaseMipLevel, LevelCount = barrier.Range.MipLevelCount, BaseArrayLayer = barrier.Range.BaseArrayLayer, LayerCount = barrier.Range.ArrayLayerCount },
        };
        var dependency = new DependencyInfo { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &memory };
        _owner.Api.CmdPipelineBarrier2(_command, &dependency);
    }

    private IRenderEncoder BeginRenderNative(RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth)
    {
        Span<RenderingAttachmentInfo> colors = stackalloc RenderingAttachmentInfo[attachments.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            RenderColorAttachmentDesc a = attachments[i];
            colors[i] = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = ((VulkanTextureView)a.View).Native,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = a.LoadOp == Abstractions.AttachmentLoadOp.Clear ? Silk.NET.Vulkan.AttachmentLoadOp.Clear : Silk.NET.Vulkan.AttachmentLoadOp.Load,
                StoreOp = a.StoreOp == Abstractions.AttachmentStoreOp.Store ? Silk.NET.Vulkan.AttachmentStoreOp.Store : Silk.NET.Vulkan.AttachmentStoreOp.DontCare,
                ClearValue = new() { Color = new ClearColorValue((float)Math.Clamp(a.ClearValue.Red, 0, 1), (float)Math.Clamp(a.ClearValue.Green, 0, 1), (float)Math.Clamp(a.ClearValue.Blue, 0, 1), (float)Math.Clamp(a.ClearValue.Alpha, 0, 1)) },
            };
        }

        RenderingAttachmentInfo depthAttachment = default;
        RenderingAttachmentInfo stencilAttachment = default;
        if (depth != null)
        {
            depthAttachment = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = ((VulkanTextureView)depth.View).Native,
                ImageLayout = ImageLayout.DepthStencilAttachmentOptimal,
                LoadOp = depth.DepthLoadOp == Abstractions.AttachmentLoadOp.Clear ? Silk.NET.Vulkan.AttachmentLoadOp.Clear : Silk.NET.Vulkan.AttachmentLoadOp.Load,
                StoreOp = depth.DepthStoreOp == Abstractions.AttachmentStoreOp.Store ? Silk.NET.Vulkan.AttachmentStoreOp.Store : Silk.NET.Vulkan.AttachmentStoreOp.DontCare,
                ClearValue = new() { DepthStencil = new(depth.DepthClearValue, depth.StencilClearValue) },
            };
            stencilAttachment = depthAttachment;
            stencilAttachment.LoadOp = depth.StencilLoadOp == Abstractions.AttachmentLoadOp.Clear ? Silk.NET.Vulkan.AttachmentLoadOp.Clear : Silk.NET.Vulkan.AttachmentLoadOp.Load;
            stencilAttachment.StoreOp = depth.StencilStoreOp == Abstractions.AttachmentStoreOp.Store ? Silk.NET.Vulkan.AttachmentStoreOp.Store : Silk.NET.Vulkan.AttachmentStoreOp.DontCare;
        }

        IGraphicsTextureView view = attachments.FirstOrDefault()?.View ?? depth!.View;
        (uint width, uint height) = view.Texture.GetMipSize(view.Info.BaseMipLevel);
        fixed (RenderingAttachmentInfo* data = colors)
        {
            var rendering = new RenderingInfo { SType = StructureType.RenderingInfo, LayerCount = 1, RenderArea = new() { Extent = new(width, height) }, ColorAttachmentCount = (uint)colors.Length, PColorAttachments = data, PDepthAttachment = depth == null ? null : &depthAttachment, PStencilAttachment = depth?.View.Info.Format == TextureFormat.Depth24Stencil8 ? &stencilAttachment : null };
            _owner.Api.CmdBeginRendering(_command, &rendering);
        }

        return new VulkanRenderEncoder(this, attachments, depth);
    }

    private IComputeEncoder BeginComputeNative() => new VulkanComputeEncoder(this);

    private void FinishNative()
    {
        Result result = _owner.Api.EndCommandBuffer(_command);
        if (result != Result.Success)
        {
            State = CommandBufferState.Faulted;
            Check(result, "EndCommandBuffer");
        }
    }

    private void DisposeNative() => _owner.Api.DestroyCommandPool(_owner.NativeDevice, _pool, null);
}
