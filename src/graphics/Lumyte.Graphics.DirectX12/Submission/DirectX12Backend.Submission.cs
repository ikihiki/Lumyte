using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private NativeQueue CreateQueue(CommandListType type)
    {
        ComPtr<ID3D12CommandQueue> queue = default;
        ComPtr<ID3D12Fence> completion = default;
        try
        {
            var description = new CommandQueueDesc(type, 0, CommandQueueFlags.None, 0);
            Check(device.CreateCommandQueue<ID3D12CommandQueue>(&description, out queue), "CreateCommandQueue");
            Check(device.CreateFence<ID3D12Fence>(0, FenceFlags.None, out completion), "CreateFence(command memory)");
            return new(this, queue, completion, type);
        }
        catch
        {
            completion.Dispose();
            queue.Dispose();
            throw;
        }
    }

    private sealed class NativeQueue(DirectX12Backend owner, ComPtr<ID3D12CommandQueue> queue,
        ComPtr<ID3D12Fence> completion, CommandListType type) : NativeGpuQueue
    {
        private ComPtr<ID3D12CommandQueue> queue = queue;
        private ComPtr<ID3D12Fence> completion = completion;
        private readonly List<PendingCommands> pending = [];
        private ulong nextSerial;
        public DirectX12Backend Owner { get; } = owner;
        public CommandListType Type { get; } = type;

        public override NativeGpuCommandBuffer StartCommandRecording()
        {
            Collect();
            return new NativeRecording(this);
        }

        public override void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuTimelinePoint signal,
            ReadOnlySpan<NativeGpuTimelinePoint> waits = default)
        {
            Collect();
            NativeSemaphore signalSemaphore = Owner.RequireSemaphore(signal.Semaphore, nameof(signal));
            var dependencies = new (NativeSemaphore Semaphore, ulong Value)[waits.Length];
            for (int index = 0; index < waits.Length; index++)
            { dependencies[index] = (Owner.RequireSemaphore(waits[index].Semaphore, nameof(waits)), waits[index].Value); }
            var records = new NativeRecording[commands.Length];
            var seen = new HashSet<NativeGpuCommandBuffer>();
            for (int index = 0; index < commands.Length; index++)
            {
                if (commands[index] is not NativeRecording recording || !ReferenceEquals(recording.Owner, this))
                { throw new ArgumentException("A recording belongs to another queue.", nameof(commands)); }
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
                // Complete all allocations and command-list validation before inserting a
                // native queue wait. Already inserted waits cannot be rolled back.
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
            foreach ((NativeSemaphore semaphore, ulong value) in dependencies)
            {
                int waitResult = queue.Wait(semaphore.Fence, value);
                if (waitResult < 0) { Owner.LoseDevice("Enqueuing a GPU timeline wait failed.", waitResult); }
            }
            if (nativeLists.Length != 0)
            {
                fixed (nint* pointers = nativeLists)
                { queue.ExecuteCommandLists(checked((uint)nativeLists.Length), (ID3D12CommandList**)pointers); }
            }

            int result = queue.Signal(completion, serial);
            if (result < 0) { Owner.LoseDevice("Signaling command-memory completion failed.", result); }
            result = queue.Signal(signalSemaphore.Fence, signal.Value);
            if (result < 0) { Owner.LoseDevice("Signaling caller completion failed.", result); }
        }

        public void VerifyOperational() => Owner.VerifyAvailable();

        private void Collect()
        {
            VerifyOperational();
            ulong finished = completion.GetCompletedValue();
            if (finished == ulong.MaxValue) { Owner.LoseDevice("Direct3D 12 reported a removed device."); }
            int count = 0;
            while (count < pending.Count && pending[count].Serial <= finished)
            {
                foreach (EncodedCommands encoded in pending[count].Commands) { encoded.Dispose(); }
                count++;
            }
            if (count != 0) { pending.RemoveRange(0, count); }
        }

        public void DisposeNativeObjects()
        {
            // The caller has completed normal work; loss has explicitly removed the device.
            foreach (PendingCommands item in pending)
            { foreach (EncodedCommands encoded in item.Commands) { encoded.Dispose(); } }
            pending.Clear();
            completion.Dispose();
            queue.Dispose();
        }

        private sealed record PendingCommands(ulong Serial, EncodedCommands[] Commands);
    }
}
