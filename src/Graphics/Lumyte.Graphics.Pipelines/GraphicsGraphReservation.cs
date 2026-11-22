using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsGraphReservation(GraphicsPipelineRecordPlan plan, GraphicsResultState state) : IDisposable
{
    private bool _committed;
    private bool _recorded;
    private bool _returned;
    private bool _claimReturned;
    private IGraphicsCommandBuffer? _commands;

    internal GraphicsRun Run => state.Run;

    public void Dispose()
    {
        if (_returned)
        {
            return;
        }

        if (_recorded && _commands?.State != CommandBufferState.Disposed)
        {
            throw new InvalidOperationException("Retain the graph use until actual GPU completion and command disposal.");
        }

        if (!_committed && !_claimReturned)
        {
            plan.CompleteGraphClaim(false);
            _claimReturned = true;
        }

        state.Dispose();
        _returned = true;
    }

    internal void Commit()
    {
        if (_returned || _committed)
        {
            throw new InvalidOperationException("Graph reservation is already consumed.");
        }

        plan.CompleteGraphClaim(true);
        _committed = true;
    }

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_returned, this);
        state.Run.Validate();
    }

    internal void Record(IGraphicsCommandBuffer commands)
    {
        Validate();
        if (!_committed || _recorded)
        {
            throw new InvalidOperationException("Graph execution requires one committed recording right.");
        }

        state.Run.ValidateRecording(commands);
        _recorded = true;
        _commands = commands;
        state.Run.Record(commands);
    }
}
