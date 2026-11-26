namespace Lumyte.Execution;

/// <summary>Orders logical stage starts against graph cancellation without creating another scheduler.</summary>
public sealed class PipelineJobScope
{
    private readonly GraphState _state;
    private bool _closed;

    internal PipelineJobScope(GraphState state)
    {
        _state = state;
    }

    /// <summary>Admits the next logical stage, or suppresses it after graph cancellation or failure.</summary>
    /// <returns>Whether the next stage may begin. Already admitted asynchronous work must still drain.</returns>
    /// <exception cref="ObjectDisposedException">The owning workflow has reached terminal completion.</exception>
    public bool TryStartLogicalStage()
    {
        lock (_state.Runtime.Sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_state.IsCancellationRequested)
            {
                _state.Run.Record(null, true);
                return false;
            }

            return true;
        }
    }

    internal void Close()
    {
        lock (_state.Runtime.Sync)
        {
            _closed = true;
        }
    }
}
