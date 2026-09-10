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

    private sealed class NativeRecording(NativeQueue owner) : NativeGpuCommandBuffer
    {
        private readonly List<Action<ComPtr<ID3D12GraphicsCommandList7>>> operations = [];
        private RecordingState state;
        public NativeQueue Owner { get; } = owner;

        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination)
        {
            VerifyRecording();
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
            VerifyRecording();
            Owner.Owner.RequireLinear(source.Region);
            Owner.Owner.RequireTexture(destination);
            operations.Add(commands => Owner.Owner.EncodeTextureCopy(commands, source, destination, footprint, upload: true));
        }

        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint)
        {
            VerifyRecording();
            Owner.Owner.RequireTexture(source);
            Owner.Owner.RequireLinear(destination.Region);
            operations.Add(commands => Owner.Owner.EncodeTextureCopy(commands, destination, source, footprint, upload: false));
        }

        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess)
        {
            VerifyRecording();
            var barrier = new GlobalBarrier(BarrierStages(beforeStages), BarrierStages(afterStages),
                BarrierAccesses(beforeAccess), BarrierAccesses(afterAccess));
            operations.Add(commands => EncodeBarrier(commands, barrier));
        }

        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout)
        {
            VerifyRecording();
            Owner.Owner.RequireTexture(view.Texture);
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
            ComPtr<ID3D12CommandAllocator> allocator = default;
            ComPtr<ID3D12GraphicsCommandList7> commands = default;
            try
            {
                Check(Owner.Owner.device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct, out allocator), "CreateCommandAllocator");
                Check(Owner.Owner.device.CreateCommandList<ID3D12CommandAllocator, ID3D12PipelineState, ID3D12GraphicsCommandList7>(
                    0, CommandListType.Direct, allocator, default, out commands), "CreateCommandList");
                foreach (Action<ComPtr<ID3D12GraphicsCommandList7>> operation in operations) { operation(commands); }
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

        public void Accept() { state = RecordingState.Accepted; operations.Clear(); }
        public void Fail() { state = RecordingState.Failed; operations.Clear(); }

        public override void Dispose()
        {
            state = RecordingState.Disposed;
            operations.Clear();
        }

        private enum RecordingState { Recording, Accepted, Failed, Disposed }
    }

    private sealed class EncodedCommands(
        ComPtr<ID3D12CommandAllocator> allocator, ComPtr<ID3D12GraphicsCommandList7> commands) : IDisposable
    {
        private ComPtr<ID3D12CommandAllocator> allocator = allocator;
        public ComPtr<ID3D12GraphicsCommandList7> Commands = commands;
        public void Dispose() { Commands.Dispose(); allocator.Dispose(); }
    }
}
