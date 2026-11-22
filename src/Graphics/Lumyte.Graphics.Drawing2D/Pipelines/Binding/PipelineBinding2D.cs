using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed partial class PipelineBinding2D
{
    private readonly IPipeline<RenderRequest2D, Device<TargetWritesV1>> _pipeline;
    private readonly SemaphoreSlim _preparation = new(1, 1);
    private readonly IGraphicsPipelineInfo _identity;
    private readonly CommandRoutes2D _routes;
    private readonly PipelineSnapshotBudget2D _snapshots;

    internal PipelineBinding2D(IGraphicDevice device, IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline, CommandRoutes2D routes, PipelinePolicy2D policy)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        _identity = pipeline as IGraphicsPipelineInfo ?? throw new ArgumentException("The pipeline does not originate from a graphics build.", nameof(pipeline));
        if (!ReferenceEquals(_identity.Device, device) || _identity.Policy != policy.Execution || _identity.InputType != typeof(RenderRequest2D) || _identity.OutputType != typeof(Device<TargetWritesV1>) || !_identity.HasTargetWritesEndpoint || device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new ArgumentException("Pipeline device, input/output identity or fixed execution policy mismatch.", nameof(pipeline));
        }

        SchemaDescriptor output = TargetWritesV1.Descriptor;
        if (output.Version != 1 || output.ElementType != typeof(uint) || output.ElementStride != sizeof(uint) || !output.Metadata.TryGetValue("dependencyOnly", out object? dependency) || !Equals(dependency, true))
        {
            throw new ArgumentException("The target endpoint is not the canonical record-dependency schema.", nameof(pipeline));
        }

        _pipeline = pipeline;
        _snapshots = new PipelineSnapshotBudget2D(policy.Retention.MaxCachedBytes);
        _routes = routes;
        Policy = policy with { Retention = policy.Retention with { } };
        RecordContext = new DirectOwnerContext();
        var adapter = new CommandAdapter(this);
        Renderers = new DrawingCommandRenderers2D(adapter, adapter, adapter, adapter, adapter, adapter, adapter, adapter, adapter, adapter, adapter);
    }

    internal PipelinePolicy2D Policy { get; }

    internal IGraphicsOwnerContext RecordContext { get; }

    internal DrawingCommandRenderers2D Renderers { get; }

    internal bool RequiresReadback => _identity.RequiresReadback;

    internal IDisposable AcquireUse() => _identity.AcquireUse();

    internal async ValueTask<IDisposable> AcquirePreparationAsync(CancellationToken token)
    {
        await _preparation.WaitAsync(token).ConfigureAwait(false);
        return new PreparationUse(_preparation);
    }

    internal PipelineSnapshotBudget2D.Use AcquireSnapshot() => _snapshots.Acquire();

    internal void ValidatePreparation() => _identity.ValidatePreparation();

    internal DrawingResources2D GetGraphCompositionResources()
    {
        StageArtifactLease artifact = _identity.GetCompiledArtifact(GraphCompositionArtifact2D.Key);
        return new DrawingResources2D(_identity.Device, artifact.Graphics ?? throw new NotSupportedException("The structural composition artifact is not a graphics program."), artifact.Sampler);
    }

    internal void ValidateCommands(IReadOnlyList<DrawingCommand2D> commands)
    {
        _identity.ValidatePreparation();
        _routes.Validate(commands);
    }

    internal async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, IReadOnlyList<DrawingCommand2D> commands, CancellationToken token)
    {
        if (RequiresReadback)
        {
            throw new NotSupportedException("This topology requires explicit PrepareExecutionAsync.");
        }

        _routes.Validate(commands);
        context.ValidateResources();
        RenderSourceBinding2D source = context.SourceBinding ?? throw new InvalidOperationException("The renderer's native source binding is missing.");
        var owners = new PipelineOwnerGroup2D();
        try
        {
            owners.Add(context.RetainResources());
            owners.Add(ImageResourceRegistry2D.Retain([new DrawList2D(commands)]));
            IDisposable? targetUse = source.AcquireTargetUse();
            if (targetUse is not null)
            {
                owners.Add(targetUse);
            }

            PipelineSnapshotBudget2D.Use snapshot = _snapshots.Acquire();
            owners.Add(snapshot);
            var request = new RenderRequest2D(context, commands, snapshot.Reserve);
            PipelineResult<Device<TargetWritesV1>> result = await _pipeline.PrepareAsync(request, token).ConfigureAwait(false);
            owners.Add(result);
            result.AddRecordValidation(context.ValidateResources);
            GraphicsPipelineRecordPlan plan = result.AcquireRecordPlan(_pipeline, source.OwnerContext);
            owners.Add(plan);
            return new PipelineBatch2D(plan, source.Target, owners);
        }
        catch (Exception error)
        {
            context.CleanupAfterFailure(owners, error);
            throw;
        }
    }

    private sealed class PreparationUse(SemaphoreSlim gate) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            if (!_returned)
            {
                gate.Release();
                _returned = true;
            }
        }
    }

    private sealed class DirectOwnerContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }
}
