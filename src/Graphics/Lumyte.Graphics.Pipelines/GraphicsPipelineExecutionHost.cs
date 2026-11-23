using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Explicitly owns segmented recording, submission receipts, completion and retirement.</summary>
public sealed class GraphicsPipelineExecutionHost : IDisposable
{
    private readonly IPipelineIdentity _pipeline;
    private readonly IGraphicsPipelineInfo _info;
    private readonly IGraphicsOwnerContext _context;
    private readonly IGraphicsDeviceRetirementAuthority? _authority;
    private readonly List<Epoch> _epochs = [];
    private readonly Func<IReadOnlyList<IGraphicsCommandBuffer>, IGraphicsSubmission> _submit;
    private GraphicsPipelineExecution? _execution;
    private bool _busy;
    private bool _disposed;
    private Exception? _primaryFailure;

    /// <summary>Initializes a new instance of the <see cref="GraphicsPipelineExecutionHost"/> class.</summary>
    /// <param name="pipeline">The exact originating compiled graphics pipeline.</param>
    /// <param name="ownerContext">The retained caller-owned dispatcher.</param>
    /// <param name="retirementAuthority">The optional matching trusted retirement authority.</param>
    public GraphicsPipelineExecutionHost(IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext, IGraphicsDeviceRetirementAuthority? retirementAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(ownerContext);
        _info = pipeline as IGraphicsPipelineInfo ?? throw new ArgumentException("Identity does not originate from a graphics build.", nameof(pipeline));
        if (!_info.HasGraphicsReadiness)
        {
            throw new ArgumentException("CPU-only pipeline identity has no graphics execution readiness.", nameof(pipeline));
        }

        if (retirementAuthority is not null && (!ReferenceEquals(retirementAuthority.Device, _info.Device) || !ReferenceEquals(retirementAuthority.Generation, _info.Generation)))
        {
            throw new ArgumentException("Retirement authority does not match the compiled device generation.", nameof(retirementAuthority));
        }

        _pipeline = pipeline;
        _context = ownerContext;
        _authority = retirementAuthority;
        _info.Generation.ValidateDevice(_info.Device);
        if (_authority is not null)
        {
            _info.Generation.ValidateAuthority(_authority);
        }

        _submit = commands => _info.Device.Queue.Submit(commands);
    }

    internal GraphicsPipelineExecutionHost(IPipelineIdentity pipeline, IGraphicsOwnerContext ownerContext, IGraphicsDeviceRetirementAuthority? retirementAuthority, Func<IReadOnlyList<IGraphicsCommandBuffer>, IGraphicsSubmission> submit)
        : this(pipeline, ownerContext, retirementAuthority)
    {
        _submit = submit;
    }

    /// <summary>Gets the retained generation and pending recovery state.</summary>
    public PipelineRecoveryState RecoveryState => new(_info.Generation, _epochs.Count(epoch => epoch.UnknownAcceptance), _epochs.Sum(epoch => (epoch.Commands is null ? 0 : 1) + (epoch.Submission is null ? 0 : 1)));

    /// <summary>Executes each epoch once, waiting for actual completion before mapping or continuing.</summary>
    /// <param name="execution">The exclusive originating execution.</param>
    /// <param name="cancellationToken">Cancellation of further CPU work and waits, not accepted GPU work.</param>
    /// <returns>Completion after the final GPU epoch and command retirement.</returns>
    public async ValueTask ExecuteAsync(GraphicsPipelineExecution execution, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        if (_busy || _execution is not null || !ReferenceEquals(execution.State.Pipeline, _pipeline) || (execution.OwnerContext is not null && !ReferenceEquals(execution.OwnerContext, _context)))
        {
            throw new InvalidOperationException("Execution identity/context mismatch or another run is still retained.");
        }

        GraphicsRun run = execution.State.Run;
        if (!ReferenceEquals(run.Generation, _info.Generation) || !ReferenceEquals(run.Device, _info.Device))
        {
            throw new InvalidOperationException("Execution generation differs from the host.");
        }

        run.Validate();
        execution.BindOwnerContext(_context);
        execution.Accept();
        _execution = execution;
        _busy = true;
        using CancellationTokenRegistration cancellation = cancellationToken.Register(() => run.AbortContinuations(new OperationCanceledException(cancellationToken)));
        try
        {
            for (int index = 0; index < run.EpochCount; index++)
            {
                await AwaitReadyAsync(run.WaitForEpochAsync(index), execution.State.Preparation, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!run.HasRecordEpoch(index))
                {
                    continue;
                }

                var epoch = new Epoch(index);
                _epochs.Add(epoch);
                await _context.InvokeAsync(() =>
                {
                    epoch.Commands = _info.Device.CreateCommandBuffer(new CommandBufferDesc { Label = $"pipeline epoch {index}" });
                    run.RecordEpoch(epoch.Commands, index);
                    epoch.Commands.Finish();
                    epoch.UnknownAcceptance = true;
                    epoch.Submission = _submit([epoch.Commands]);
                    epoch.UnknownAcceptance = false;
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
                await epoch.Submission!.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await _context.InvokeAsync(() => run.MapEpochAsync(index)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await RetireAsync(epoch).ConfigureAwait(false);
                _epochs.Remove(epoch);
                cancellationToken.ThrowIfCancellationRequested();
                await _context.InvokeAsync(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    run.PublishEpoch(index, cancellationToken);
                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
            }

            await execution.State.Preparation.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await _context.InvokeAsync(() =>
            {
                run.PublishImmutableUploads();
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
            execution.Complete();
            _execution = null;
        }
        catch (Exception error)
        {
            _primaryFailure = error;
            run.AbortContinuations(error);
            throw;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Retries known accepted completion and cleanup without cancellation.</summary>
    /// <returns>Completion when no uncertain epoch or failed cleanup remains.</returns>
    public async ValueTask DrainAsync()
    {
        if (_busy)
        {
            throw new InvalidOperationException("Cannot drain while ExecuteAsync is active.");
        }

        var errors = new List<Exception>();
        for (int i = _epochs.Count - 1; i >= 0; i--)
        {
            Epoch epoch = _epochs[i];
            if (epoch.UnknownAcceptance)
            {
                continue;
            }

            try
            {
                if (epoch.Submission is not null && !epoch.RetirementConfirmed)
                {
                    await epoch.Submission.WaitAsync().ConfigureAwait(false);
                }

                await RetireAsync(epoch).ConfigureAwait(false);
                _epochs.RemoveAt(i);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (_epochs.Any(epoch => epoch.UnknownAcceptance))
        {
            errors.Add(new PipelineQuarantinedException(RecoveryState));
        }

        if (_epochs.Count == 0 && _execution is not null)
        {
            try
            {
                await _execution.State.Preparation.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // CPU failure is already reported by ExecuteAsync; retirement does not imply success.
            }

            _execution.Complete();
            _execution = null;
        }

        if (errors.Count != 0)
        {
            if (_primaryFailure is not null)
            {
                errors.Insert(0, _primaryFailure);
            }

            throw new AggregateException(errors);
        }

        _primaryFailure = null;
    }

    /// <summary>Recovers uncertain owners only using the originating trusted retirement proof.</summary>
    /// <param name="proof">The unforgeable proof for this exact generation.</param>
    /// <returns>The retryable cleanup completion; CPU work is never resumed.</returns>
    public async ValueTask RecoverAsync(GraphicsDeviceRetirementProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (_authority is null || !ReferenceEquals(proof.Generation, _info.Generation) || !ReferenceEquals(_authority.Generation, _info.Generation))
        {
            throw new ArgumentException("Proof does not belong to the host's fixed trusted retirement authority.", nameof(proof));
        }

        proof.ValidateFor(_authority, _info.Device, _info.Generation);
        if (_busy)
        {
            throw new InvalidOperationException("Cannot recover while execution is active.");
        }

        foreach (Epoch epoch in _epochs)
        {
            epoch.UnknownAcceptance = false;
            epoch.RetirementConfirmed = true;
        }

        await DrainAsync().ConfigureAwait(false);
    }

    /// <summary>Disposes only a fully drained host, without implicit waiting or device destruction.</summary>
    public void Dispose()
    {
        if (_busy || _execution is not null || _epochs.Count != 0)
        {
            throw new InvalidOperationException("Retain and explicitly drain the unfinished execution host.");
        }

        _disposed = true;
    }

    private static async Task AwaitReadyAsync(Task readiness, Task preparation, CancellationToken token)
    {
        Task completed = await Task.WhenAny(readiness, preparation).WaitAsync(token).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        if (!readiness.IsCompleted)
        {
            await readiness.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private ValueTask RetireAsync(Epoch epoch)
        => _context.InvokeAsync(() =>
        {
            epoch.Commands?.Dispose();
            epoch.Commands = null;
            epoch.Submission?.Dispose();
            epoch.Submission = null;
            return ValueTask.CompletedTask;
        });

    private sealed class Epoch(int index)
    {
        internal int Index { get; } = index;

        internal IGraphicsCommandBuffer? Commands { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        internal bool UnknownAcceptance { get; set; }

        internal bool RetirementConfirmed { get; set; }
    }
}
