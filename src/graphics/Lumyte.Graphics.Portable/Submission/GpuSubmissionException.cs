namespace Lumyte.Graphics.Portable;

/// <summary>A submission failed after the runtime handoff may have begun.</summary>
/// <remarks>
/// Completion identifies the affected submission; it does not prove GPU use has ended.
/// Runtime connection failure may prevent this point from completing. Do not retry the work
/// or reclaim its resources merely because this exception was thrown.
/// </remarks>
public sealed class GpuSubmissionException : InvalidOperationException
{
    public GpuSubmissionException(GpuFenceValue completion, Exception innerException)
        : base($"GPU submission at timeline value {completion.Value} failed during or after runtime handoff.",
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        if (completion.Semaphore is null)
        { throw new ArgumentException("A submission completion requires a timeline.", nameof(completion)); }
        Completion = completion;
    }

    public GpuFenceValue Completion { get; }
}
