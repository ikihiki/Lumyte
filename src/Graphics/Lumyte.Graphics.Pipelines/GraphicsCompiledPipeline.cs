using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsCompiledPipeline<TInput, TOutput>(CompiledPipeline<TInput, TOutput> core, IGraphicDevice device, GraphicsDeviceGeneration generation, GraphicsPipelinePolicy policy, IDisposable[] artifacts, int[] nodeEpochs, bool hasReadback, bool hasTargetWritesEndpoint, bool hasGraphicsReadiness) : IPipeline<TInput, TOutput>, IGraphicsPipelineInfo
{
    private readonly object _gate = new();
    private readonly GraphicsPipelinePolicy _policy = policy;
    private readonly GraphicsScratchBudget _scratchBudget = new(policy.MaxScratchBytes);
    private readonly ImmutableUploadCache? _immutableUploads = policy.ImmutableUploads is { } retention ? new(retention) : null;
    private readonly List<IDisposable> _artifacts = [.. artifacts];
    private readonly List<GraphicsRun> _cleanup = [];
    private int _uses;
    private bool _disposed;

    public ImmutableUploadCache? ImmutableUploads => _immutableUploads;

    public IGraphicDevice Device => device;

    public GraphicsDeviceGeneration Generation => generation;

    public GraphicsPipelinePolicy Policy => _policy;

    public bool RequiresReadback => hasReadback;

    public bool HasTargetWritesEndpoint => hasTargetWritesEndpoint;

    public bool HasGraphicsReadiness => hasGraphicsReadiness;

    public Type InputType => typeof(TInput);

    public Type OutputType => typeof(TOutput);

    public void ValidatePreparation()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The originating device is no longer ready.");
            }
        }
    }

    public StageArtifactLease GetCompiledArtifact(StageArtifactKey key)
    {
        ValidatePreparation();
        StageArtifactLease artifact = _artifacts.OfType<StageArtifactLease>().SingleOrDefault(candidate => candidate.Description.Key == key) ?? throw new NotSupportedException("The compiled pipeline did not declare this structural artifact.");
        artifact.Validate();
        return artifact;
    }

    public IDisposable AcquireUse()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var use = new Use(this);
            _uses = checked(_uses + 1);
            return use;
        }
    }

    public async ValueTask<PipelineResult<TOutput>> PrepareAsync(TInput input, CancellationToken cancellationToken = default)
    {
        GraphicsPipelinePolicy runPolicy = _policy;
        if (input is IGraphicsPreparationBudget admission)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(admission.MaxPreparationBytes);
            runPolicy = new(Math.Min(_policy.MaxScratchBytes, admission.MaxPreparationBytes), _policy.MaxTransferBytes, _policy.MaxReadbackBytes, _policy.MaxEpochs);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _uses++;
        }

        var run = new GraphicsRun(device, generation, runPolicy, nodeEpochs, _scratchBudget, _immutableUploads);
        try
        {
            Task<PipelineResult<TOutput>> preparation = CompleteCoreAsync();
            if (hasReadback)
            {
                await Task.WhenAny(preparation, run.WaitForEpochAsync(0)).ConfigureAwait(false);
            }

            if (!hasReadback || preparation.IsCompleted)
            {
                await preparation.ConfigureAwait(false);
            }

            var state = new GraphicsResultState(this, run, new Use(this), preparation, hasReadback, hasGraphicsReadiness);
            return new PipelineResult<TOutput>(default!, state, state, () =>
            {
                if (!preparation.IsCompleted)
                {
                    throw new InvalidOperationException("The output requires explicit segmented execution before CPU access.");
                }

                return preparation.GetAwaiter().GetResult().Value;
            });

            async Task<PipelineResult<TOutput>> CompleteCoreAsync()
            {
                PipelineResult<TOutput> result = await core.PrepareRunAsync(input, run, cancellationToken).ConfigureAwait(false);
                run.Own(result);
                return result;
            }
        }
        catch (Exception primary)
        {
            try
            {
                run.Dispose();
                new Use(this).Dispose();
            }
            catch (Exception cleanup)
            {
                lock (_gate)
                {
                    _cleanup.Add(run);
                }

                throw new AggregateException(primary, cleanup);
            }

            throw;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            core.Dispose();
            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                _cleanup[i].Dispose();
                _cleanup.RemoveAt(i);
                _uses--;
            }

            if (_uses == 0)
            {
                Cleanup();
            }
        }
    }

    private void Cleanup()
    {
        _immutableUploads?.Dispose();
        for (int i = _artifacts.Count - 1; i >= 0; i--)
        {
            _artifacts[i].Dispose();
            _artifacts.RemoveAt(i);
        }
    }

    private sealed class Use(GraphicsCompiledPipeline<TInput, TOutput> pipeline) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            lock (pipeline._gate)
            {
                if (_returned)
                {
                    return;
                }

                if (pipeline._disposed && pipeline._uses == 1)
                {
                    pipeline.Cleanup();
                }

                pipeline._uses--;
                _returned = true;
            }
        }
    }
}
