using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics execution.</summary>
public sealed class GraphicsExecution : IDisposable
{
    private readonly GraphicsDeviceSession _session;
    private readonly PreparedGraphicsExecution _prepared;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _submitAttempted;
    private bool _completed;
    private bool _successful;

    internal GraphicsExecution(GraphicsDeviceSession session, PreparedGraphicsExecution prepared)
    {
        (_session, _prepared) = (session, prepared);
    }

    /// <summary>Gets recovery.</summary>
    public GraphicsRecoveryState Recovery { get; private set; } = GraphicsRecoveryState.Prepared;

    internal bool IsDisposed => Recovery == GraphicsRecoveryState.Retired;

    /// <summary>Declares record async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask RecordAsync(CancellationToken cancellationToken = default) => _session.Execution.RunAsync(Record, cancellationToken: cancellationToken);

    /// <summary>Declares record.</summary>
    public void Record()
    {
        _session.Execution.VerifyAccess();
        if (Recovery != GraphicsRecoveryState.Prepared)
        {
            throw new InvalidOperationException("Record requires prepared state.");
        }

        _commands = _prepared.Commands ?? throw new InvalidOperationException("Prepared command allocation is missing.");
        Recovery = GraphicsRecoveryState.Recording;
        try
        {
            foreach (PlanPass pass in _prepared.Compiled.Passes)
            {
                foreach (PreparedPlanBarrier barrier in _prepared.PreparedBarriers[pass])
                {
                    barrier.Record(_commands);
                }

                foreach (PlanCommand command in pass.Commands)
                {
                    command.Record(_commands);
                }
            }

            foreach (PreparedPlanBarrier barrier in _prepared.Resources.FinalBarriers)
            {
                barrier.Record(_commands);
            }

            Recovery = GraphicsRecoveryState.Recorded;
        }
        catch
        {
            Recovery = GraphicsRecoveryState.CleanupPending;
            throw;
        }
    }

    /// <summary>Declares finish async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask FinishAsync(CancellationToken cancellationToken = default) => _session.Execution.RunAsync(Finish, cancellationToken: cancellationToken);

    /// <summary>Declares finish.</summary>
    public void Finish()
    {
        _session.Execution.VerifyAccess();
        if (Recovery != GraphicsRecoveryState.Recorded)
        {
            throw new InvalidOperationException("Finish requires successfully recorded commands.");
        }

        try
        {
            _commands!.Finish();
            Recovery = GraphicsRecoveryState.Finished;
        }
        catch
        {
            Recovery = GraphicsRecoveryState.CleanupPending;
            throw;
        }
    }

    /// <summary>Declares submit async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask SubmitAsync(CancellationToken cancellationToken = default) => _session.Execution.RunAsync(Submit, cancellationToken: cancellationToken);

    /// <summary>Declares submit.</summary>
    public void Submit()
    {
        _session.Execution.VerifyAccess();
        if (Recovery != GraphicsRecoveryState.Finished)
        {
            throw new InvalidOperationException("Submit requires finished commands.");
        }

        _submitAttempted = true;
        Recovery = GraphicsRecoveryState.Quarantined;
        _submission = _session.Device.Queue.Submit([_commands!]);
        Recovery = GraphicsRecoveryState.Submitted;
    }

    /// <summary>Declares wait async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        ValueTask wait = default;
        try
        {
            await _session.Execution.RunAsync(
                () =>
                {
                    if (_submission is null)
                    {
                        throw new InvalidOperationException("No known submission; verified device retirement is required.");
                    }

                    wait = _submission.WaitAsync(cancellationToken);
                },
                retiring: true).ConfigureAwait(false);
            await wait.ConfigureAwait(false);
        }
        catch
        {
            await _session.Execution.RunAsync(() => Recovery = GraphicsRecoveryState.Quarantined, retiring: true).ConfigureAwait(false);
            throw;
        }

        await _session.Execution.RunAsync(
            () =>
        {
            if (_submission!.Status != SubmissionStatus.Completed || _commands!.State != CommandBufferState.Completed)
            {
                Recovery = GraphicsRecoveryState.Quarantined;
                throw new InvalidOperationException("GPU and command completion are not established.");
            }

            _completed = true;
            _successful = true;
            Recovery = GraphicsRecoveryState.Completed;
        },
            retiring: true).ConfigureAwait(false);
    }

    /// <summary>Declares execute async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The declared result.</returns>
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RecordAsync(cancellationToken).ConfigureAwait(false);
        await FinishAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await SubmitAsync(cancellationToken).ConfigureAwait(false);
        await WaitAsync(cancellationToken).ConfigureAwait(false);
        await RetireAsync().ConfigureAwait(false);
    }

    /// <summary>Declares retire async.</summary>
    /// <returns>The declared result.</returns>
    public ValueTask RetireAsync() => _session.Execution.RunAsync(Dispose, retiring: true);

    /// <summary>Declares dispose.</summary>
    public void Dispose()
    {
        _session.Execution.VerifyAccess();
        if (IsDisposed)
        {
            return;
        }

        if (_submitAttempted && !_completed)
        {
            throw new InvalidOperationException("In-flight or acceptance-unknown execution is quarantined; Drain or verified device retirement is required.");
        }

        Recovery = GraphicsRecoveryState.CleanupPending;

        // Command retirement precedes submission handles, tables, views and allocations.
        if (_commands is not null)
        {
            _commands.Dispose();
            _commands = null;
            _prepared.Commands = null;
        }

        if (_submission is not null)
        {
            _submission.Dispose();
            _submission = null;
        }

        _prepared.Retire(_successful);
        Recovery = GraphicsRecoveryState.Retired;
        _session.ReleaseExecution(this);
    }

    internal async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return;
        }

        if (_submitAttempted && !_completed)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        await RetireAsync().ConfigureAwait(false);
    }

    internal void RecoverRetiredDevice()
    {
        _completed = true;
        Dispose();
    }
}
