using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private PortableQueue? mainQueue;
    private readonly HashSet<SubmittedCommands> pendingCommands = [];

    public P.IGpuQueue MainQueue
    {
        get { lock (gate) { RequireAvailable(); return mainQueue!; } }
    }

    private void InitializeQueue()
    {
        F.QueueHandle handle = F.WebGPU_FFI.DeviceGetQueue(device);
        RequireNativeObject((nuint)handle, "queue");
        try { mainQueue = new PortableQueue(this, handle); }
        catch { F.WebGPU_FFI.QueueRelease(handle); throw; }
    }

    private sealed class PortableQueue(WebGpuBackend owner, F.QueueHandle handle) : P.IGpuQueue
    {
        internal readonly WebGpuBackend Owner = owner;
        internal readonly F.QueueHandle Handle = handle;

        public P.GpuCommandBuffer StartCommandRecording()
        {
            lock (Owner.gate) { Owner.RequireAvailable(); return new CommandRecording(this); }
        }

        public P.GpuSemaphore CreateSemaphore(ulong initialValue = 0)
        {
            lock (Owner.gate) { Owner.RequireAvailable(); return new WebGpuTimeline(this, Owner.status, initialValue); }
        }

        public void Submit(ReadOnlySpan<P.GpuCommandBuffer> commandBuffers, P.GpuSemaphore signalSemaphore, ulong signalValue)
            => Owner.Submit(this, commandBuffers, signalSemaphore, signalValue);

        public bool IsComplete(P.GpuSemaphore semaphore, ulong value) => RequireTimeline(semaphore).IsComplete(value);

        public ValueTask WaitAsync(P.GpuSemaphore semaphore, ulong value, CancellationToken cancellationToken = default)
            => RequireTimeline(semaphore).WaitAsync(value, cancellationToken);

        internal WebGpuTimeline RequireTimeline(P.GpuSemaphore semaphore)
        {
            ArgumentNullException.ThrowIfNull(semaphore);
            if (semaphore is not WebGpuTimeline timeline)
            { throw new ArgumentException("The timeline belongs to another queue.", nameof(semaphore)); }
            timeline.VerifyOwner(this);
            return timeline;
        }
    }

    private sealed class SubmittedCommands(CommandRecording[] recordings, F.CommandBufferHandle[] handles)
    {
        internal readonly F.CommandBufferHandle[] Handles = handles;
        private bool released;

        internal void Release()
        {
            if (released) { return; }
            released = true;
            foreach (F.CommandBufferHandle handle in Handles)
            {
                if ((nuint)handle != 0) { F.WebGPU_FFI.CommandBufferRelease(handle); }
            }
            foreach (CommandRecording recording in recordings) { recording.Release(); }
        }
    }

    private unsafe void Submit(PortableQueue queue, ReadOnlySpan<P.GpuCommandBuffer> commandBuffers,
        P.GpuSemaphore signalSemaphore, ulong signalValue)
    {
        if (commandBuffers.IsEmpty) { throw new ArgumentException("A batch requires at least one recording.", nameof(commandBuffers)); }
        var recordings = new CommandRecording[commandBuffers.Length];
        var handles = new F.CommandBufferHandle[commandBuffers.Length];
        var unique = new HashSet<CommandRecording>();
        var dependencies = new HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>>();
        lock (gate)
        {
            RequireAvailable();
            WebGpuTimeline timeline = queue.RequireTimeline(signalSemaphore);
            timeline.ValidateSignal(signalValue);
            for (int index = 0; index < commandBuffers.Length; index++)
            {
                if (commandBuffers[index] is not CommandRecording recording || !ReferenceEquals(recording.Queue, queue))
                { throw new ArgumentException("Every recording must belong to the submitting queue.", nameof(commandBuffers)); }
                recording.ValidateSubmission();
                if (!unique.Add(recording)) { throw new ArgumentException("A recording may occur only once in a batch.", nameof(commandBuffers)); }
                recordings[index] = recording;
            }
            var submitted = new SubmittedCommands(recordings, handles);
            foreach (CommandRecording recording in recordings) { recording.Consume(); }
            bool reserved = false;
            bool handedToQueue = false;
            try
            {
                foreach (CommandRecording recording in recordings)
                {
                    PrepareComputePipelines(recording, dependencies);
                    PrepareRasterPipelines(recording, dependencies);
                }
                for (int index = 0; index < recordings.Length; index++) { handles[index] = Encode(recordings[index], dependencies); }
                pendingCommands.Add(submitted);
                timeline.Reserve(signalValue);
                reserved = true;
                PushScopes();
                try
                {
                    fixed (F.CommandBufferHandle* pointer = handles)
                    {
                        handedToQueue = true;
                        F.WebGPU_FFI.QueueSubmit(queue.Handle, (nuint)handles.Length, pointer);
                    }
                }
                finally { dependencies.Add(PopScopes()); }
                Task gpuEnded = TrackCompletion(queue, submitted);
                timeline.Accept(signalValue, gpuEnded, WebGpuDiagnostics.CombineAsync(dependencies.ToArray()));
            }
            catch (Exception error)
            {
                // Once submission may have happened, its acceptance cannot be rolled back or retried safely.
                if (reserved) { status.Lose($"WebGPU submission connection failed: {error.Message}"); }
                if (!handedToQueue)
                {
                    pendingCommands.Remove(submitted);
                    submitted.Release();
                }
                throw;
            }
        }
    }

    private unsafe Task TrackCompletion(PortableQueue queue, SubmittedCommands commands)
    {
        var result = new WebGpuCallbacks.Result<(N.QueueWorkDoneStatus, string)>();
        bool submitted = false;
        try
        {
            N.Future future = F.WebGPU_FFI.QueueOnSubmittedWorkDone(queue.Handle, new()
            {
                Mode = N.CallbackMode.AllowSpontaneous,
                Callback = &WebGpuCallbacks.WorkDone,
                Userdata1 = result.Allocate(),
            });
            submitted = true;
            events!.Register(future, result.Task);
        }
        catch
        {
            if (!submitted) { result.Release(); }
            throw;
        }
        return FinishSubmittedCommandsAsync(commands, result.Task);
    }

    private async Task FinishSubmittedCommandsAsync(SubmittedCommands commands,
        Task<(N.QueueWorkDoneStatus Status, string Message)> completion)
    {
        try
        {
            var result = await completion.ConfigureAwait(false);
            if (result.Status != N.QueueWorkDoneStatus.Success)
            {
                status.Lose($"WebGPU queue completion failed ({result.Status}): {result.Message}");
                status.ThrowIfFailed();
            }
            lock (gate)
            {
                status.ThrowIfFailed();
                pendingCommands.Remove(commands);
                commands.Release();
            }
        }
        catch (Exception error)
        {
            status.Lose($"WebGPU queue completion connection failed: {error.Message}");
            // Unknown GPU completion retains command memory until device shutdown.
            status.ThrowIfFailed();
            throw;
        }
    }

    private void StopQueue()
    {
        foreach (SubmittedCommands commands in pendingCommands) { commands.Release(); }
        pendingCommands.Clear();
        if (mainQueue != null)
        {
            F.WebGPU_FFI.QueueRelease(mainQueue.Handle);
            mainQueue = null;
        }
    }

    internal int PendingCommandCount { get { lock (gate) { return pendingCommands.Count; } } }
}
