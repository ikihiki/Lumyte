using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private readonly object gate = new();
    private PortableQueue? mainQueue;
    private readonly HashSet<SubmittedCommands> pendingCommands = [];
    public P.IGpuQueue MainQueue { get { RequireAvailable(); return mainQueue!; } }
    private void InitializeQueue() => mainQueue = new(this);

    private sealed class PortableQueue(WebGpuBackend owner) : P.IGpuQueue
    {
        internal readonly WebGpuBackend Owner = owner;
        public P.GpuCommandBuffer StartCommandRecording() { Owner.RequireAvailable(); return new CommandRecording(this); }
        public P.GpuSemaphore CreateSemaphore(ulong initialValue = 0) { Owner.RequireAvailable(); return new BrowserTimeline(this, Owner.status, initialValue); }
        public void Submit(ReadOnlySpan<P.GpuCommandBuffer> commands, P.GpuSemaphore semaphore, ulong value)
            => Owner.Submit(this, commands, semaphore, value);
        public bool IsComplete(P.GpuSemaphore semaphore, ulong value) => RequireTimeline(semaphore).IsComplete(value);
        public ValueTask WaitAsync(P.GpuSemaphore semaphore, ulong value, CancellationToken cancellationToken = default)
            => RequireTimeline(semaphore).WaitAsync(value, cancellationToken);
        internal BrowserTimeline RequireTimeline(P.GpuSemaphore semaphore)
        {
            ArgumentNullException.ThrowIfNull(semaphore);
            if (semaphore is not BrowserTimeline timeline) { throw new ArgumentException("The timeline belongs to another queue.", nameof(semaphore)); }
            timeline.VerifyOwner(this);
            return timeline;
        }
    }

    private sealed class SubmittedCommands(CommandRecording[] recordings, JSObject[] handles)
    {
        internal readonly JSObject[] Handles = handles;
        private bool released;
        internal void Release()
        {
            if (released) { return; }
            released = true;
            foreach (JSObject? handle in Handles) { handle?.Dispose(); }
            foreach (CommandRecording recording in recordings) { recording.Release(); }
        }
    }

    private void Submit(PortableQueue queue, ReadOnlySpan<P.GpuCommandBuffer> commandBuffers, P.GpuSemaphore semaphore, ulong value)
    {
        RequireAvailable();
        if (commandBuffers.IsEmpty) { throw new ArgumentException("A batch requires at least one recording.", nameof(commandBuffers)); }
        BrowserTimeline timeline = queue.RequireTimeline(semaphore);
        timeline.ValidateSignal(value);
        var recordings = new CommandRecording[commandBuffers.Length];
        var handles = new JSObject[commandBuffers.Length];
        var unique = new HashSet<CommandRecording>();
        var dependencies = new HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>>();
        for (int index = 0; index < recordings.Length; index++)
        {
            if (commandBuffers[index] is not CommandRecording recording || !ReferenceEquals(recording.Queue, queue))
            { throw new ArgumentException("Every recording must belong to the submitting queue.", nameof(commandBuffers)); }
            recording.ValidateSubmission();
            if (!unique.Add(recording)) { throw new ArgumentException("A recording may occur only once in a batch.", nameof(commandBuffers)); }
            recordings[index] = recording;
        }
        var submitted = new SubmittedCommands(recordings, handles);
        var observation = new WebGpuSubmission(new(timeline, value));
        foreach (CommandRecording recording in recordings) { recording.Consume(); }
        bool reserved = false;
        try
        {
            foreach (CommandRecording recording in recordings) { PreparePipelines(recording, dependencies); }
            for (int index = 0; index < recordings.Length; index++) { handles[index] = Encode(recordings[index], dependencies); }
            pendingCommands.Add(submitted);
            timeline.Reserve(value);
            reserved = true;
            BrowserInterop.PushErrorScopes(device);
            observation.Execute(
                () => timeline.Accept(value, observation.GpuEnded, observation.Diagnostics),
                () => BrowserInterop.Submit(device, handles),
                () => FinishSubmittedCommandsAsync(submitted, BrowserInterop.OnSubmittedWorkDoneAsync(device)),
                () =>
                {
                    dependencies.Add(ReadDiagnosticsAsync(BrowserInterop.PopErrorScopesAsync(device)));
                    return BrowserDiagnostics.CombineAsync(dependencies.ToArray());
                });
        }
        catch (Exception error)
        {
            if (reserved) { status.Lose($"Browser WebGPU submission connection failed: {error.Message}"); }
            observation.ReleaseUnsubmitted(() => { pendingCommands.Remove(submitted); submitted.Release(); });
            throw;
        }
    }

    private async Task FinishSubmittedCommandsAsync(SubmittedCommands commands, Task completion)
    {
        try
        {
            await completion;
            runtime.RequireThread();
            status.ThrowIfFailed();
            pendingCommands.Remove(commands);
            commands.Release();
        }
        catch (Exception error)
        {
            status.Lose($"Browser WebGPU queue completion failed: {error.Message}");
            status.ThrowIfFailed();
            throw;
        }
    }

    private void StopQueue()
    {
        foreach (SubmittedCommands commands in pendingCommands) { commands.Release(); }
        pendingCommands.Clear();
        mainQueue = null;
    }
}
