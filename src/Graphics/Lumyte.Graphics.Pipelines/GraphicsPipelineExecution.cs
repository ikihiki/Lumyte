namespace Lumyte.Graphics.Pipelines;

/// <summary>Owns one exclusive explicit segmented execution use.</summary>
public sealed class GraphicsPipelineExecution : IDisposable
{
    private GraphicsResultState? _state;
    private bool _accepted;
    private bool _completed;

    internal GraphicsPipelineExecution(GraphicsResultState state, IGraphicsOwnerContext? context)
    {
        _state = state;
        OwnerContext = context;
    }

    internal IGraphicsOwnerContext? OwnerContext { get; private set; }

    internal GraphicsResultState State => _state ?? throw new ObjectDisposedException(nameof(GraphicsPipelineExecution));

    /// <summary>Aborts an unaccepted preparation, or returns this use after explicit execution and command retirement.</summary>
    /// <remarks>Disposal never submits or waits. If an unaccepted last owner still has CPU continuations, pump the borrowed runtime and retry disposal. Accepted work remains owned by its host until Drain or trusted recovery completes.</remarks>
    public void Dispose()
    {
        if (_state is null)
        {
            return;
        }

        if (_accepted && !_completed)
        {
            throw new InvalidOperationException("The execution host still owns an unfinished execution.");
        }

        if (!_accepted)
        {
            _state.AbortUnaccepted();
        }

        _state.Dispose();
        _state = null;
    }

    internal void BindOwnerContext(IGraphicsOwnerContext context)
    {
        if (OwnerContext is not null && !ReferenceEquals(OwnerContext, context))
        {
            throw new InvalidOperationException("Execution is already bound to another owner context.");
        }

        OwnerContext = context;
    }

    internal void Accept()
    {
        if (_accepted)
        {
            throw new InvalidOperationException("An execution can be accepted exactly once.");
        }

        State.MarkAccepted();
        _accepted = true;
    }

    internal void Complete() => _completed = true;
}
