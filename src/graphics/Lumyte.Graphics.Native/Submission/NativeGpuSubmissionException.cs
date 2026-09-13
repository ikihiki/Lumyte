namespace Lumyte.Graphics.Native;

/// <summary>A submission failed after GPU handoff occurred or may have occurred.</summary>
/// <remarks>
/// The requested completion identifies this submission; its value is not guaranteed to be reached.
/// This exception does not prove that GPU use has ended. Keep referenced resources alive until
/// their use has ended or device shutdown has been confirmed. Do not resubmit the recordings.
/// An unclassified exception is not, by itself, proof that no GPU handoff occurred.
/// </remarks>
public sealed class NativeGpuSubmissionException : Exception
{
    public NativeGpuSubmissionException(NativeGpuTimelinePoint completion, Exception innerException)
        : base("Native GPU submission failed after GPU handoff occurred or may have occurred.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        Completion = completion;
    }

    /// <summary>The caller-requested signal, without a guarantee that it will be reached.</summary>
    public NativeGpuTimelinePoint Completion { get; }
}
