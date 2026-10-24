using System.Threading.Tasks.Sources;
using Lumyte.Execution;

namespace Lumyte.Resources.Execution;

/// <summary>Dispatches runtime-contiguous pipelines and their await continuations through the host's CPU job scheduler.</summary>
/// <remarks>Share one instance across managers on the same host and register it with the host's execution modules.</remarks>
public sealed class ResourceRuntime : IResourceRuntime, IExecutionModule
{
    private readonly WorkflowPort<BuildRequest, object> _build = new();
    private readonly WorkflowPort<ReleaseRequest, Unit> _release = new();

    /// <inheritdoc/>
    public string Name => "Resources";

    /// <inheritdoc/>
    public ValueTask<object> BuildAsync(IResourceStage stage, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var request = new BuildRequest(stage, context);
        CommandEnqueueStatus status = _build.TryEnqueue(in request, cancellationToken, out IValueTaskSource<object>? completion);
        return status switch
        {
            CommandEnqueueStatus.Accepted => new ValueTask<object>(completion!, 0),
            CommandEnqueueStatus.Canceled => ValueTask.FromCanceled<object>(cancellationToken),
            CommandEnqueueStatus.NotRunning => ValueTask.FromException<object>(new InvalidOperationException("Start the resource execution runtime before loading resources.")),
            CommandEnqueueStatus.QueueFull => ValueTask.FromException<object>(new ExecutionQueueFullException("Resource workflow capacity is exhausted.")),
            _ => throw new InvalidOperationException("Unknown resource workflow admission status."),
        };
    }

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(IResourceStage stage, object resource)
    {
        var request = new ReleaseRequest(stage, resource);
        CommandEnqueueStatus status = _release.TryEnqueue(in request, CancellationToken.None, out IValueTaskSource<Unit>? completion);
        if (status != CommandEnqueueStatus.Accepted)
        {
            throw status == CommandEnqueueStatus.QueueFull
                ? new ExecutionQueueFullException("Resource workflow capacity is exhausted.")
                : new InvalidOperationException("Keep the resource execution runtime running until resource cleanup completes.");
        }

        await new ValueTask<Unit>(completion!, 0).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        builder.Workflow<BuildRequest, object, BuildHandler>("Build", default, _build);
        builder.Workflow<ReleaseRequest, Unit, ReleaseHandler>("Release", default, _release);
    }

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    private readonly record struct BuildRequest(IResourceStage Stage, ResourceBuildContext Context);

    private readonly record struct ReleaseRequest(IResourceStage Stage, object Resource);

    private readonly struct BuildHandler : IWorkflowHandler<BuildRequest, object>
    {
        public ValueTask<object> ExecuteAsync(BuildRequest command, CancellationToken cancellationToken)
            => command.Context.ExecutePipelineAsync(command.Stage, cancellationToken);
    }

    private readonly struct ReleaseHandler : IWorkflowHandler<ReleaseRequest, Unit>
    {
        public async ValueTask<Unit> ExecuteAsync(ReleaseRequest command, CancellationToken cancellationToken)
        {
            await command.Stage.ReleaseAsync(command.Resource);
            return default;
        }
    }
}
