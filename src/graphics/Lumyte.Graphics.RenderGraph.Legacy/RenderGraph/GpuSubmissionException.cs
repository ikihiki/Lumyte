namespace Lumyte.Graphics.RenderGraph.Legacy;

/// <summary>A submission started before an error was reported. Completion still owns its resources.</summary>
public sealed class GpuSubmissionException : Exception, IDisposable
{
    private GpuRetirementQueue? ownedQueue;

    internal GpuSubmissionException(GpuSubmissionToken completion, Exception innerException)
        : base("GPU work was submitted before an error occurred. Wait for Completion before releasing its resources.", innerException)
        => Completion = completion;

    public GpuSubmissionToken Completion { get; }

    internal void OwnQueue(GpuRetirementQueue queue) => ownedQueue = queue;

    /// <summary>Drains failed work and disposes any queue created by synchronous graph execution.</summary>
    public void Dispose()
    {
        if (ownedQueue is { } queue) { queue.Dispose(); ownedQueue = null; }
        else { Completion.Wait(); }
    }
}
