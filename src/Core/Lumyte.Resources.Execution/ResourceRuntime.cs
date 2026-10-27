using System.Threading.Tasks.Sources;
using Lumyte.Execution;

namespace Lumyte.Resources.Execution;

/// <summary>Dispatches runtime-contiguous pipelines and their await continuations through the host's CPU job scheduler.</summary>
/// <remarks>Share one instance across managers on the same host and register it with the host's execution modules.</remarks>
public sealed class ResourceRuntime : IResourceRuntime, IResourceExecutor, IExecutionModule
{
    private readonly WorkflowPort<BuildRequest, Unit> _build = new();
    private readonly WorkflowPort<ReleaseRequest, Unit> _release = new();
    private readonly WorkflowPort<StreamRequest, Unit> _stream = new();

    /// <inheritdoc/>
    public IResourceExecutor Executor => this;

    /// <inheritdoc/>
    public bool ShouldYield => ExecutionRuntime.ShouldYield(TimeSpan.FromMilliseconds(1));

    /// <inheritdoc/>
    public string Name => "Resources";

    /// <inheritdoc/>
    public async ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var request = new BuildRequest(context);
        CommandEnqueueStatus status = _build.Enqueue(in request, out IValueTaskSource<Unit>? completion, cancellationToken);
        if (status != CommandEnqueueStatus.Accepted)
        {
            if (status == CommandEnqueueStatus.Canceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw status == CommandEnqueueStatus.QueueFull
                ? new ExecutionQueueFullException("Resource workflow capacity is exhausted.")
                : new InvalidOperationException("Start the resource execution runtime before loading resources.");
        }

        await new ValueTask<Unit>(completion!, 0).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource)
    {
        var request = new ReleaseRequest(stage, runtime, resource);
        CommandEnqueueStatus status = _release.Enqueue(in request, out IValueTaskSource<Unit>? completion, CancellationToken.None);
        if (status != CommandEnqueueStatus.Accepted)
        {
            throw status == CommandEnqueueStatus.QueueFull
                ? new ExecutionQueueFullException("Resource workflow capacity is exhausted.")
                : new InvalidOperationException("Keep the resource execution runtime running until resource cleanup completes.");
        }

        await new ValueTask<Unit>(completion!, 0).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask StreamAsync(IResourceStreamWork work, CancellationToken cancellationToken)
    {
        var request = new StreamRequest(work);
        CommandEnqueueStatus status = _stream.Enqueue(in request, out IValueTaskSource<Unit>? completion, cancellationToken);
        if (status != CommandEnqueueStatus.Accepted)
        {
            if (status == CommandEnqueueStatus.Canceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw status == CommandEnqueueStatus.QueueFull
                ? new ExecutionQueueFullException("Resource workflow capacity is exhausted.")
                : new InvalidOperationException("Keep the resource execution runtime running while streaming resources.");
        }

        await new ValueTask<Unit>(completion!, 0).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        builder.Workflow<BuildRequest, Unit, BuildHandler>("Build", default, _build);
        builder.Workflow<ReleaseRequest, Unit, ReleaseHandler>("Release", default, _release);
        builder.Workflow<StreamRequest, Unit, StreamHandler>("Stream", default, _stream);
    }

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    private readonly record struct BuildRequest(ResourceBuildContext Context);

    private readonly record struct ReleaseRequest(IResourceStage Stage, IResourceRuntime Runtime, object Resource);

    private readonly record struct StreamRequest(IResourceStreamWork Work);

    private readonly struct BuildHandler : IWorkflowHandler<BuildRequest, Unit>
    {
        public async ValueTask<Unit> ExecuteAsync(BuildRequest command, CancellationToken cancellationToken)
        {
            await command.Context.ExecuteAsync(cancellationToken);
            return default;
        }
    }

    private readonly struct ReleaseHandler : IWorkflowHandler<ReleaseRequest, Unit>
    {
        public async ValueTask<Unit> ExecuteAsync(ReleaseRequest command, CancellationToken cancellationToken)
        {
            await command.Stage.ReleaseAsync(command.Resource, command.Runtime);
            return default;
        }
    }

    private readonly struct StreamHandler : IWorkflowHandler<StreamRequest, Unit>
    {
        public async ValueTask<Unit> ExecuteAsync(StreamRequest command, CancellationToken cancellationToken)
        {
            await command.Work.ExecuteAsync(cancellationToken);
            return default;
        }
    }
}
