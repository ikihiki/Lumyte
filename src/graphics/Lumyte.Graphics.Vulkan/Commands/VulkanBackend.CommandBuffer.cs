using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private sealed record CommandSegment(CommandBuffer Command, TextureRecord? InitializationCandidate);

    private sealed class CommandRecord : NativeGpuCommandBuffer
    {
        private CommandPool pool;
        private readonly List<CommandSegment> segments;
        private readonly HashSet<TextureRecord> touched = [];
        private bool ended;
        private bool submitted;
        private bool disposed;
        private NativeDescriptorHeapBindings descriptorHeaps;
        private Pipeline computePipeline;

        public CommandRecord(QueueRecord queue, CommandPool pool, CommandBuffer first)
        {
            Queue = queue;
            this.pool = pool;
            segments = [new(first, null)];
        }

        public QueueRecord Queue { get; }
        public IReadOnlyList<CommandSegment> Segments => segments;
        private VulkanBackend Owner => Queue.Owner;
        private CommandBuffer Current => segments[^1].Command;

        public override void SetComputePipeline(NativeGpuComputePipelineHandle pipeline)
        {
            VerifyRecording();
            computePipeline = Owner.RequireComputePipeline(pipeline).Pipeline;
            Owner.vk.CmdBindPipeline(Current, PipelineBindPoint.Compute, computePipeline);
        }

        public override void Dispatch(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1)
        {
            VerifyRecording();
            PushRoot(rootData);
            Owner.vk.CmdDispatch(Current, x, y, z);
        }

        public override void DispatchIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments)
        {
            VerifyRecording();
            Owner.RequireCommandRange(arguments);
            if (arguments.Size < 12)
            {
                throw new ArgumentException("Indirect dispatch arguments must cover three 32-bit group counts.", nameof(arguments));
            }
            NativeDispatchIndirectInfo info = new()
            {
                SType = (StructureType)1000318011,
                AddressRange = new() { Address = arguments.GpuAddress, Size = arguments.Size },
                AddressFlags = LinearAddressFlags,
            };
            PushRoot(rootData);
            Owner.dispatchIndirect(Current, &info);
        }

        private void PushRoot(ReadOnlySpan<byte> rootData)
        {
            if (rootData.IsEmpty) { return; }
            fixed (byte* data = rootData)
            {
                NativePushDataInfo info = new()
                {
                    SType = (StructureType)1000135004,
                    Data = new() { Address = data, Size = checked((nuint)rootData.Length) },
                };
                Owner.pushData(Current, &info);
            }
        }

        public override void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap)
        {
            VerifyRecording();
            DescriptorHeapRecord record = Owner.RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
            descriptorHeaps.Resource = record.BindInfo;
            NativeBindHeapInfo info = record.BindInfo;
            Owner.bindResourceHeap(Current, &info);
        }

        public override void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap)
        {
            VerifyRecording();
            DescriptorHeapRecord record = Owner.RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Sampler);
            descriptorHeaps.Sampler = record.BindInfo;
            NativeBindHeapInfo info = record.BindInfo;
            Owner.bindSamplerHeap(Current, &info);
        }

        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination)
        {
            VerifyRecording();
            Owner.RequireCommandRange(source);
            Owner.RequireCommandRange(destination);
            if (source.Size > destination.Size)
            {
                throw new ArgumentException("The destination range must cover the source size.", nameof(destination));
            }
            NativeDeviceMemoryCopy region = new()
            {
                SType = (StructureType)1000318000,
                Source = new() { Address = source.GpuAddress, Size = source.Size }, SourceFlags = LinearAddressFlags,
                // Pass only the bytes being written, not the destination's spare logical capacity.
                Destination = new() { Address = destination.GpuAddress, Size = source.Size }, DestinationFlags = LinearAddressFlags,
            };
            NativeCopyDeviceMemoryInfo info = new() { SType = (StructureType)1000318001, RegionCount = 1, Regions = &region };
            Queue.CopyMemory(Current, &info);
        }

        public override void CopyMemoryToTexture(NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint)
        {
            VerifyRecording();
            Owner.RequireCommandRange(source);
            TextureRecord texture = Owner.RequireCommandTexture(destination);
            NativeDeviceMemoryImageCopy region = TextureCopyRegion(texture.Description.Format, source, footprint);
            Touch(texture);
            NativeCopyDeviceMemoryImageInfo info = new()
            {
                SType = (StructureType)1000318003, Image = texture.Image, RegionCount = 1, Regions = &region,
            };
            Queue.CopyMemoryToImage(Current, &info);
        }

        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint)
        {
            VerifyRecording();
            Owner.RequireCommandRange(destination);
            TextureRecord texture = Owner.RequireCommandTexture(source);
            NativeDeviceMemoryImageCopy region = TextureCopyRegion(texture.Description.Format, destination, footprint);
            Touch(texture);
            NativeCopyDeviceMemoryImageInfo info = new()
            {
                SType = (StructureType)1000318003, Image = texture.Image, RegionCount = 1, Regions = &region,
            };
            Queue.CopyImageToMemory(Current, &info);
        }

        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess)
        {
            VerifyRecording();
            MemoryBarrier2 barrier = new()
            {
                SType = StructureType.MemoryBarrier2,
                SrcStageMask = CommandStages(beforeStages), SrcAccessMask = CommandAccess(beforeAccess),
                DstStageMask = CommandStages(afterStages), DstAccessMask = CommandAccess(afterAccess),
            };
            DependencyInfo dependency = new()
            {
                SType = StructureType.DependencyInfo, MemoryBarrierCount = 1, PMemoryBarriers = &barrier,
            };
            Owner.vk.CmdPipelineBarrier2(Current, &dependency);
        }

        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout)
        {
            VerifyRecording();
            Owner.RequireCommandTexture(view.Texture);
            throw new NotSupportedException("Vulkan Native textures use GENERAL. ExplicitTextureTransitions is unavailable.");
        }

        public override void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout)
        {
            VerifyRecording();
            TextureRecord texture = Owner.RequireCommandTexture(view.Texture);
            if (afterLayout != GpuTextureLayout.General)
            {
                throw new ArgumentException("Vulkan Native discard requires GENERAL.", nameof(afterLayout));
            }
            if (texture.Description.Dimension == NativeGpuTextureDimension.ThreeD && view.Dimension != NativeGpuTextureViewDimension.ThreeD)
            {
                throw new ArgumentException("A 3D image discard requires a ThreeD view covering whole mip volumes; depth slices cannot be discarded independently.", nameof(view));
            }
            ImageSubresourceRange range = new(TextureAspects(view.Aspect), view.BaseMip, view.MipCount, view.BaseLayer, view.LayerCount);
            Touch(texture);
            Owner.RecordDiscard(Current, texture, range);
        }

        private void Touch(TextureRecord texture)
        {
            if (!texture.RequiresGeneralInitialization || touched.Contains(texture)) { return; }
            // Leave all segments recording until Submit has validated the whole batch. A new native
            // buffer preserves this precise point for the conditional initialization command.
            segments.EnsureCapacity(checked(segments.Count + 1));
            touched.EnsureCapacity(checked(touched.Count + 1));
            CommandSegment segment = new(Queue.AllocateCommand(pool), texture);
            segments.Add(segment);
            touched.Add(texture);
            // Each segment is a new primary command buffer with no inherited binding state.
            descriptorHeaps.Apply(Current, Owner.bindResourceHeap, Owner.bindSamplerHeap);
            if (computePipeline.Handle != 0)
            {
                Owner.vk.CmdBindPipeline(Current, PipelineBindPoint.Compute, computePipeline);
            }
        }

        public void VerifyRecording()
        {
            Queue.VerifyAvailable();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (ended || submitted) { throw new InvalidOperationException("A command recording can be submitted only once."); }
            ObjectDisposedException.ThrowIf(pool.Handle == 0, this);
        }

        public void End()
        {
            ended = true;
            foreach (CommandSegment segment in segments)
            {
                Queue.CheckResult(Owner.vk.EndCommandBuffer(segment.Command), "vkEndCommandBuffer");
            }
        }

        public void Reject() => ended = true;
        public void MarkSubmitted() => submitted = true;

        public override void Dispose()
        {
            if (disposed) { return; }
            if (!submitted && pool.Handle != 0) { Owner.VerifyNotDisposed(); }
            disposed = true;
            if (!submitted) { ReleaseNative(); }
        }

        public void ReleaseNative()
        {
            if (pool.Handle == 0) { return; }
            Owner.vk.DestroyCommandPool(Owner.device, pool, null);
            pool = default;
            segments.Clear();
            touched.Clear();
        }
    }
}
