namespace Lumyte.UI.Navigation;

/// <summary>A request whose completion never faults for navigation failure or cancellation.</summary>
public sealed class UiNavigationRequest
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<UiNavigationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<UiVisualTransitionResult> _visual = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _skip;
    private int _settled;

    /// <summary>Gets the nonreused request identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the request's result, not resource retirement completion.</summary>
    public Task<UiNavigationResult> Completion => _completion.Task;

    /// <summary>Gets the nonfaulting visual result, independently of logical commit.</summary>
    public Task<UiVisualTransitionResult> VisualCompletion => _visual.Task;

    internal bool SkipRequested => Volatile.Read(ref _skip) != 0;

    internal CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Signals cancellation before commit; may be called from any thread.</summary>
    public void Cancel()
    {
        if (Volatile.Read(ref _settled) == 0)
        {
            _cancellation.Cancel();
        }
    }

    /// <summary>Requests skipping optional effects without canceling navigation or returning to an old page.</summary>
    public void SkipAnimation()
    {
        if (!_visual.Task.IsCompleted)
        {
            Interlocked.Exchange(ref _skip, 1);
        }
    }

    internal void CompleteVisual(UiVisualTransitionResult result) => _visual.TrySetResult(result);

    internal void Complete(UiNavigationResult result)
    {
        Interlocked.Exchange(ref _settled, 1);
        _completion.TrySetResult(result);
        if (result.Status != UiNavigationStatus.Committed)
        {
            CompleteVisual(new(UiVisualTransitionStatus.NotStarted, "not-committed"));
        }
    }
}
