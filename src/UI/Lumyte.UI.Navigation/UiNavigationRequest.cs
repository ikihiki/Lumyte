namespace Lumyte.UI.Navigation;

/// <summary>A request whose completion never faults for navigation failure or cancellation.</summary>
public sealed class UiNavigationRequest
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<UiNavigationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _settled;

    /// <summary>Gets the nonreused request identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the request's result, not resource retirement completion.</summary>
    public Task<UiNavigationResult> Completion => _completion.Task;

    internal CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Signals cancellation before commit; may be called from any thread.</summary>
    public void Cancel()
    {
        if (Volatile.Read(ref _settled) == 0)
        {
            _cancellation.Cancel();
        }
    }

    internal void Complete(UiNavigationResult result)
    {
        Interlocked.Exchange(ref _settled, 1);
        _completion.TrySetResult(result);
    }
}
