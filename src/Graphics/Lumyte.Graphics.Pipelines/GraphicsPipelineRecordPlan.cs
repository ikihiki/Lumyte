using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Owns one exclusive record-only use of a prepared graphics pipeline.</summary>
public sealed class GraphicsPipelineRecordPlan : IDisposable
{
    private readonly object _gate = new();
    private GraphicsResultState? _state;
    private int _graphClaim;
    private IGraphicsCommandBuffer? _commands;
    private bool _recorded;
    private bool _recordSucceeded;

    internal GraphicsPipelineRecordPlan(GraphicsResultState state, IGraphicsOwnerContext context)
    {
        _state = state;
        OwnerContext = context;
        Resources = state.Run.CaptureManifest();
    }

    /// <summary>Gets the complete immutable external resource manifest.</summary>
    public GraphicsPipelineResourceManifest Resources { get; }

    internal IGraphicsOwnerContext OwnerContext { get; }

    internal GraphicsResultState State => _state ?? throw new ObjectDisposedException(nameof(GraphicsPipelineRecordPlan));

    /// <summary>Records once without Finish, Submit, waiting or mapping.</summary>
    /// <param name="commands">The idle borrowed recording command.</param>
    public void Record(IGraphicsCommandBuffer commands)
    {
        lock (_gate)
        {
            ArgumentNullException.ThrowIfNull(commands);
            ObjectDisposedException.ThrowIf(_state is null, this);
            if (_recorded || _graphClaim != 0 || commands.State != CommandBufferState.Recording)
            {
                throw new InvalidOperationException("The plan can be recorded exactly once into a recording command.");
            }

            _state.Run.ValidateRecording(commands);
            _recorded = true;
            _commands = commands;
            _state.Run.Record(commands);
            _recordSucceeded = true;
        }
    }

    /// <summary>Publishes immutable uploads only after the recorded submission completed and its command memory was disposed.</summary>
    /// <remarks>The caller supplies the actual receipt from submitting this plan's recorded command, not an unrelated completed submission. Unknown acceptance never qualifies.</remarks>
    /// <param name="submission">The actual successful submission containing this plan's command.</param>
    public void RetireImmutableUploads(IGraphicsSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state is null, this);
            if (!_recordSucceeded || _commands?.State != CommandBufferState.Disposed || submission.Status != SubmissionStatus.Completed || _state.Run.Device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("Immutable publication requires successful recording, actual completion, disposed command memory and the ready originating generation.");
            }

            _state.Run.PublishImmutableUploads();
        }
    }

    /// <summary>Returns the independent use only after any recorded command has been safely disposed.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_graphClaim == 1)
            {
                throw new InvalidOperationException("A graph append reservation is still in progress.");
            }

            if (_state is null)
            {
                return;
            }

            if (_commands is not null && _commands.State != CommandBufferState.Disposed)
            {
                throw new InvalidOperationException("Retain the plan until actual GPU completion and command disposal.");
            }

            _state.Dispose();
            _state = null;
        }
    }

    internal KeyValuePair<object, long> CaptureWorkingReservation()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state is null, this);
            return new(_state.Run, _state.Run.ReservedWorkingBytes);
        }
    }

    internal void ValidateGraphAppend()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state is null, this);
            if (_recorded || _graphClaim != 0)
            {
                throw new InvalidOperationException("The record plan is already assigned or recorded.");
            }

            _state.Run.Validate();
        }
    }

    internal GraphicsGraphReservation ReserveGraph()
    {
        lock (_gate)
        {
            ValidateGraphAppend();
            var reservation = new GraphicsGraphReservation(this, _state!);
            _state!.Retain();
            _graphClaim = 1;
            return reservation;
        }
    }

    internal void CompleteGraphClaim(bool committed)
    {
        lock (_gate)
        {
            if (_graphClaim != 1)
            {
                throw new InvalidOperationException("The graph claim is not reserved.");
            }

            _graphClaim = committed ? 2 : 0;
        }
    }
}
