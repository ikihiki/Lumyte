using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private LinearRecord RequireLinear(NativeGpuLinearRegion region)
    {
        if (region is not LinearRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The linear region belongs to another device.", nameof(region));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, region);
        return record;
    }

    private TextureRecord RequireTexture(NativeGpuTextureHandle texture)
    {
        if (texture is not TextureRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("The texture belongs to another device.", nameof(texture));
        }
        ObjectDisposedException.ThrowIf(record.Disposed, texture);
        return record;
    }

    private sealed partial class NativeRecording(NativeQueue owner) : NativeGpuCommandBuffer
    {
        private readonly List<Action<ComPtr<ID3D12GraphicsCommandList8>>> operations = [];
        private RecordingState state;
        private NativeGpuDescriptorHeap? resourceHeap;
        private NativeGpuDescriptorHeap? samplerHeap;
        public NativeQueue Owner { get; } = owner;

        public override void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap)
        {
            VerifyRecording();
            Owner.Owner.RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
            AddDescriptorHeaps(heap, samplerHeap);
            resourceHeap = heap;
        }

        public override void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap)
        {
            VerifyRecording();
            Owner.Owner.RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Sampler);
            AddDescriptorHeaps(resourceHeap, heap);
            samplerHeap = heap;
        }

        private void AddDescriptorHeaps(NativeGpuDescriptorHeap? resource, NativeGpuDescriptorHeap? sampler)
        {
            // SetDescriptorHeaps replaces both selections. Capture their values for this
            // point in the recording, without owning either heap or its referenced resources.
            operations.Add(commands =>
            {
                ID3D12DescriptorHeap** heaps = stackalloc ID3D12DescriptorHeap*[2];
                uint count = 0;
                if (resource is not null) { heaps[count++] = Owner.Owner.RequireDescriptorHeap(resource).Heap.Handle; }
                if (sampler is not null) { heaps[count++] = Owner.Owner.RequireDescriptorHeap(sampler).Heap.Handle; }
                commands.SetDescriptorHeaps(count, heaps);
            });
        }

        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination)
        {
            VerifyOutsideRendering();
            Owner.Owner.RequireLinear(source.Region);
            Owner.Owner.RequireLinear(destination.Region);
            if (source.Size > destination.Size)
            {
                throw new ArgumentException("The destination range must cover the source bytes.", nameof(destination));
            }
            operations.Add(commands =>
            {
                LinearRecord src = Owner.Owner.RequireLinear(source.Region);
                LinearRecord dst = Owner.Owner.RequireLinear(destination.Region);
                commands.CopyBufferRegion(dst.Resource, destination.Offset, src.Resource, source.Offset, source.Size);
            });
        }

        public override void CopyMemoryToTexture(NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint)
        {
            VerifyOutsideRendering();
            Owner.Owner.RequireLinear(source.Region);
            Owner.Owner.RequireTexture(destination);
            operations.Add(commands => Owner.Owner.EncodeTextureCopy(commands, source, destination, footprint, upload: true));
        }

        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint)
        {
            VerifyOutsideRendering();
            Owner.Owner.RequireTexture(source);
            Owner.Owner.RequireLinear(destination.Region);
            operations.Add(commands => Owner.Owner.EncodeTextureCopy(commands, destination, source, footprint, upload: false));
        }

        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess)
        {
            VerifyOutsideRendering();
            var barrier = new GlobalBarrier(BarrierStages(beforeStages), BarrierStages(afterStages),
                BarrierAccesses(beforeAccess), BarrierAccesses(afterAccess));
            operations.Add(commands => EncodeBarrier(commands, barrier));
        }

        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout)
        {
            VerifyOutsideRendering();
            TextureRecord texture = Owner.Owner.RequireTexture(view.Texture);
            RequireTransitionView(view, texture.Description.Dimension);
            operations.Add(commands => Owner.Owner.EncodeTransition(commands, view, beforeLayout, afterLayout));
        }

        public override void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout)
            => TextureTransition(view, GpuTextureLayout.Undefined, afterLayout);

        public void VerifyRecording()
        {
            Owner.VerifyOperational();
            if (state != RecordingState.Recording)
            {
                throw new InvalidOperationException("The recording is no longer available for recording or submission.");
            }
        }

        public EncodedCommands Encode()
        {
            if (rendering) { throw new InvalidOperationException("End rendering before submitting the recording."); }
            ComPtr<ID3D12CommandAllocator> allocator = default;
            ComPtr<ID3D12GraphicsCommandList8> commands = default;
            try
            {
                Check(Owner.Owner.device.CreateCommandAllocator<ID3D12CommandAllocator>(Owner.Type, out allocator), "CreateCommandAllocator");
                Check(Owner.Owner.device.CreateCommandList<ID3D12CommandAllocator, ID3D12PipelineState, ID3D12GraphicsCommandList8>(
                    0, Owner.Type, allocator, default, out commands), "CreateCommandList");
                foreach (Action<ComPtr<ID3D12GraphicsCommandList8>> operation in operations) { operation(commands); }
                Check(commands.Close(), "ID3D12GraphicsCommandList.Close");
                return new(allocator, commands);
            }
            catch
            {
                commands.Dispose();
                allocator.Dispose();
                throw;
            }
        }

        public void Accept() { state = RecordingState.Accepted; ClearOperations(); }
        public void Fail() { state = RecordingState.Failed; ClearOperations(); }

        public override void Dispose()
        {
            state = RecordingState.Disposed;
            ClearOperations();
        }

        private void ClearOperations() { operations.Clear(); resourceHeap = null; samplerHeap = null; computePipeline = null; rasterPipeline = null; }

        private enum RecordingState { Recording, Accepted, Failed, Disposed }
    }

    private sealed class EncodedCommands(
        ComPtr<ID3D12CommandAllocator> allocator, ComPtr<ID3D12GraphicsCommandList8> commands) : IDisposable
    {
        private ComPtr<ID3D12CommandAllocator> allocator = allocator;
        public ComPtr<ID3D12GraphicsCommandList8> Commands = commands;
        public void Dispose() { Commands.Dispose(); allocator.Dispose(); }
    }
}
