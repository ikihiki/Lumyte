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
        internal ID3D12CommandQueue* Handle => queue.Handle;
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
                if (!seen.Add(recording))
                { throw new ArgumentException("A recording occurs more than once.", nameof(commands)); }
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
                foreach (EncodedCommands? item in encoded)
                { item?.Dispose(); }
                foreach (NativeRecording record in records)
                { record.Fail(); }
                throw;
            }

            foreach (NativeRecording record in records)
            { record.Accept(); }
            nextSerial = serial;
            DirectX12Submission.Execute(signal, dependencies.Length,
                new SubmissionCalls(this, dependencies, nativeLists, signalSemaphore, signal.Value, serial));
        }

        private readonly struct SubmissionCalls(NativeQueue submissionQueue,
            (NativeSemaphore Semaphore, ulong Value)[] dependencies, nint[] nativeLists,
            NativeSemaphore signalSemaphore, ulong signalValue, ulong serial) : IDirectX12SubmissionCalls
        {
            public void Wait(int index)
            {
                (NativeSemaphore semaphore, ulong value) = dependencies[index];
                int result = submissionQueue.queue.Wait(semaphore.Fence, value);
                if (result < 0)
                { submissionQueue.Owner.LoseDevice("Enqueuing a GPU timeline wait failed.", result); }
            }

            public void ExecuteCommands()
            {
                if (nativeLists.Length == 0)
                { return; }
                fixed (nint* pointers = nativeLists)
                { submissionQueue.queue.ExecuteCommandLists((uint)nativeLists.Length, (ID3D12CommandList**)pointers); }
            }

            public void SignalInternal()
            {
                int result = submissionQueue.queue.Signal(submissionQueue.completion, serial);
                if (result < 0)
                { submissionQueue.Owner.LoseDevice("Signaling command-memory completion failed.", result); }
            }

            public void SignalCaller()
            {
                int result = submissionQueue.queue.Signal(signalSemaphore.Fence, signalValue);
                if (result < 0)
                { submissionQueue.Owner.LoseDevice("Signaling caller completion failed.", result); }
            }

            public void Fault() => submissionQueue.Owner.submissionFaulted = true;
        }

        public void VerifyOperational() => Owner.VerifyAvailable();

        internal void DrainSurfaceUse()
        {
            VerifyOperational();
            ulong serial = checked(++nextSerial);
            Owner.CheckDeviceResult(queue.Signal(completion, serial), "Signal(presentation completion)");
            Owner.CheckDeviceResult(completion.SetEventOnCompletion(serial, (void*)null), "Wait(presentation completion)");
            Collect();
        }

        internal void Collect()
        {
            VerifyOperational();
            ulong finished = completion.GetCompletedValue();
            if (finished == ulong.MaxValue)
            { Owner.LoseDevice("Direct3D 12 reported a removed device."); }
            int count = 0;
            while (count < pending.Count && pending[count].Serial <= finished)
            {
                foreach (EncodedCommands encoded in pending[count].Commands)
                { encoded.Dispose(); }
                count++;
            }
            if (count != 0)
            { pending.RemoveRange(0, count); }
        }

        public void DisposeNativeObjects()
        {
            // The caller must have ended GPU use. Submission failure is not completion proof.
            foreach (PendingCommands item in pending)
            { foreach (EncodedCommands encoded in item.Commands) { encoded.Dispose(); } }
            pending.Clear();
            completion.Dispose();
            queue.Dispose();
        }

        private sealed record PendingCommands(ulong Serial, EncodedCommands[] Commands);
    }
}
