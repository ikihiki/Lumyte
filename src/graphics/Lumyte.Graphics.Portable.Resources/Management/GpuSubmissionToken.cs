namespace Lumyte.Graphics.Portable.Resources;

/// <summary>A manager-issued observation identity, without raw timeline signal authority.</summary>
public readonly struct GpuSubmissionToken : IEquatable<GpuSubmissionToken>
{
    internal GpuSubmissionToken(ManagedSubmission submission) { Submission = submission; }
    internal ManagedSubmission? Submission { get; }
    public bool IsValid => Submission is not null;
    /// <summary>True means GPU use ended; it does not imply successful execution.</summary>
    public bool IsComplete => Submission?.Poll() ?? false;
    public ValueTask WaitAsync(CancellationToken cancellationToken = default)
        => Get().WaitAsync(cancellationToken);
    private ManagedSubmission Get() => Submission ?? throw new InvalidOperationException("The submission token is invalid.");
    public bool Equals(GpuSubmissionToken other) => ReferenceEquals(Submission, other.Submission);
    public override bool Equals(object? obj) => obj is GpuSubmissionToken other && Equals(other);
    public override int GetHashCode() => Submission?.GetHashCode() ?? 0;
    public static bool operator ==(GpuSubmissionToken left, GpuSubmissionToken right) => left.Equals(right);
    public static bool operator !=(GpuSubmissionToken left, GpuSubmissionToken right) => !left.Equals(right);
}

/// <summary>Submission may have reached the GPU. Completion remains observable using the manager token.</summary>
public sealed class GpuSubmissionException : InvalidOperationException
{
    internal GpuSubmissionException(GpuSubmissionToken completion, Exception cause)
        : base("GPU submission did not establish successful handoff. Resources remain retained until GPU use ends.", cause)
    { Completion = completion; }
    public GpuSubmissionToken Completion { get; }
}

/// <summary>GPU use ended but execution diagnostics did not establish success.</summary>
public sealed class GpuExecutionException : InvalidOperationException
{
    internal GpuExecutionException(IReadOnlyList<GpuDiagnostic> diagnostics)
        : base($"GPU execution failed: {string.Join("; ", diagnostics.Select(item => item.Message))}")
    { Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); }
    public IReadOnlyList<GpuDiagnostic> Diagnostics { get; }
}

internal sealed class ManagedSubmission(GpuResourceManager manager, GpuFenceValue point)
{
    internal GpuResourceManager Manager { get; } = manager;
    internal GpuFenceValue Point { get; } = point;
    internal TaskCompletionSource Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal volatile bool OutcomeObserved;
    internal volatile bool Ended;
    internal bool Poll()
    {
        if (Ended) { return true; }
        if (Manager.Queue.IsComplete(Point.Semaphore, Point.Value)) { Ended = true; }
        return Ended;
    }
    internal async Task ObserveAsync()
    {
        try
        {
            await Manager.Queue.WaitAsync(Point.Semaphore, Point.Value);
            Ended = true;
            Outcome.TrySetResult();
        }
        catch (Portable.GpuExecutionException error)
        {
            Ended = true;
            Outcome.TrySetException(new GpuExecutionException(error.Diagnostics));
        }
        catch (Exception error) { Outcome.TrySetException(Sanitize(error)); }
    }
    internal async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        try { await Outcome.Task.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { OutcomeObserved = true; throw; }
        OutcomeObserved = true;
    }
    internal static Exception Sanitize(Exception error) => error switch
    {
        Portable.GpuSubmissionException submitted => Sanitize(submitted.InnerException!),
        Portable.GpuExecutionException executed => new GpuExecutionException(executed.Diagnostics),
        AggregateException aggregate => new AggregateException(aggregate.InnerExceptions.Select(Sanitize)),
        // Arbitrary wrappers may carry a raw fence-bearing inner exception. Preserve the outer
        // message without exporting the borrowed manager's private signaling object.
        _ when error.InnerException is not null => new InvalidOperationException(error.Message, Sanitize(error.InnerException)),
        _ => error,
    };
}
