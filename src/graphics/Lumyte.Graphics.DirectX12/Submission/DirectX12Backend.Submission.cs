using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private NativeQueue CreateMainQueue()
    {
        ComPtr<ID3D12CommandQueue> queue = default;
        ComPtr<ID3D12Fence> completion = default;
        try
        {
            var description = new CommandQueueDesc(CommandListType.Direct, 0, CommandQueueFlags.None, 0);
            Check(device.CreateCommandQueue<ID3D12CommandQueue>(&description, out queue), "CreateCommandQueue");
            Check(device.CreateFence<ID3D12Fence>(0, FenceFlags.None, out completion), "CreateFence(command memory)");
            return new(this, queue, completion);
        }
        catch
        {
            completion.Dispose();
            queue.Dispose();
            throw;
        }
    }

    private sealed class NativeQueue(
        DirectX12Backend owner, ComPtr<ID3D12CommandQueue> queue, ComPtr<ID3D12Fence> completion) : NativeGpuQueue
    {
        private ComPtr<ID3D12CommandQueue> queue = queue;
        private ComPtr<ID3D12Fence> completion = completion;
        private readonly List<PendingCommands> pending = [];
        private ulong nextSerial;
        public DirectX12Backend Owner { get; } = owner;

        public override NativeGpuCommandBuffer StartCommandRecording()
        {
            Collect();
            return new NativeRecording(this);
        }

        public override NativeGpuSemaphore CreateSemaphore(ulong initialValue)
        {
            Collect();
            ComPtr<ID3D12Fence> fence = default;
            try
            {
                Check(Owner.device.CreateFence<ID3D12Fence>(initialValue, FenceFlags.None, out fence), "CreateFence");
                return new NativeSemaphore(this, fence);
            }
            catch
            {
                fence.Dispose();
                throw;
            }
        }

        public override void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuSemaphore semaphore, ulong value)
        {
            Collect();
            NativeSemaphore signal = RequireSemaphore(semaphore);
            if (commands.IsEmpty) { throw new ArgumentException("A submission requires at least one recording.", nameof(commands)); }
            var records = new NativeRecording[commands.Length];
            var seen = new HashSet<NativeGpuCommandBuffer>();
            for (int index = 0; index < commands.Length; index++)
            {
                if (commands[index] is not NativeRecording recording || !ReferenceEquals(recording.Owner, this))
                {
                    throw new ArgumentException("A recording belongs to another queue.", nameof(commands));
                }
                if (!seen.Add(recording)) { throw new ArgumentException("A recording occurs more than once.", nameof(commands)); }
                recording.VerifyRecording();
                records[index] = recording;
            }

            ulong serial = checked(nextSerial + 1);
            var encoded = new EncodedCommands[records.Length];
            var nativeLists = new nint[records.Length];
            try
            {
                for (int index = 0; index < records.Length; index++)
                {
                    encoded[index] = records[index].Encode();
                    nativeLists[index] = (nint)encoded[index].Commands.Handle;
                }
                // Allocate tracking before native acceptance; no caller semaphore is retained.
                pending.Add(new(serial, encoded));
            }
            catch
            {
                foreach (EncodedCommands? item in encoded) { item?.Dispose(); }
                foreach (NativeRecording record in records) { record.Fail(); }
                throw;
            }

            foreach (NativeRecording record in records) { record.Accept(); }
            nextSerial = serial;
            fixed (nint* pointers = nativeLists)
            {
                queue.ExecuteCommandLists(checked((uint)nativeLists.Length), (ID3D12CommandList**)pointers);
            }

            int result = queue.Signal(completion, serial);
            if (result < 0) { LoseDevice("Signaling command-memory completion failed.", result); }
            result = queue.Signal(signal.Fence, value);
            if (result < 0) { LoseDevice("Signaling caller completion failed.", result); }
        }

        public override bool IsComplete(NativeGpuSemaphore semaphore, ulong value)
        {
            Collect();
            NativeSemaphore signal = RequireSemaphore(semaphore);
            ulong completed = signal.Fence.GetCompletedValue();
            if (completed == ulong.MaxValue) { LoseDevice("Direct3D 12 reported a removed device."); }
            return completed >= value;
        }

        public override void Wait(NativeGpuSemaphore semaphore, ulong value)
        {
            Collect();
            NativeSemaphore signal = RequireSemaphore(semaphore);
            // A null event handle makes this explicit wait block inside the native runtime.
            Check(signal.Fence.SetEventOnCompletion(value, (void*)null), "SetEventOnCompletion");
            if (signal.Fence.GetCompletedValue() == ulong.MaxValue)
            {
                LoseDevice("Direct3D 12 reported a removed device while waiting.");
            }
            Collect();
        }

        public void VerifyOperational()
        {
            Owner.VerifyAvailable();
        }

        private void Collect()
        {
            VerifyOperational();
            ulong finished = completion.GetCompletedValue();
            if (finished == ulong.MaxValue) { LoseDevice("Direct3D 12 reported a removed device."); }
            int count = 0;
            while (count < pending.Count && pending[count].Serial <= finished)
            {
                foreach (EncodedCommands encoded in pending[count].Commands) { encoded.Dispose(); }
                count++;
            }
            if (count != 0) { pending.RemoveRange(0, count); }
        }

        private NativeSemaphore RequireSemaphore(NativeGpuSemaphore semaphore)
        {
            if (semaphore is not NativeSemaphore native || !ReferenceEquals(native.Owner, this))
            {
                throw new ArgumentException("The semaphore belongs to another queue.", nameof(semaphore));
            }
            ObjectDisposedException.ThrowIf(native.Disposed, semaphore);
            return native;
        }

        private void LoseDevice(string message, int? result = null)
        {
            Owner.deviceLoss = message;
            // Signal failure after acceptance must not leave unobservable GPU work running.
            // Remove the device before constructing diagnostic text, which itself may allocate.
            Owner.device10.RemoveDevice();
            throw new GpuDeviceLostException(result is int error ? $"{message} HRESULT 0x{error:X8}." : message);
        }

        public void DisposeNativeObjects()
        {
            // The caller has completed normal work; loss has explicitly removed the device.
            foreach (PendingCommands item in pending)
            {
                foreach (EncodedCommands encoded in item.Commands) { encoded.Dispose(); }
            }
            pending.Clear();
            completion.Dispose();
            queue.Dispose();
        }

        private sealed record PendingCommands(ulong Serial, EncodedCommands[] Commands);
    }

}
