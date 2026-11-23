using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Directly binds an explicitly built generic pipeline without a public strategy conversion.</summary>
public sealed partial class Renderer2D : IAsyncPipelineRenderer2D
{
    private readonly PipelineBinding2D? _pipelineBinding;

    /// <summary>Initializes a new instance of the <see cref="Renderer2D"/> class from an exact compiled generic pipeline.</summary>
    /// <param name="device">The borrowed matching native device.</param>
    /// <param name="pipeline">The directly borrowed graphics-built pipeline.</param>
    /// <param name="routes">The complete explicit command acceptance table.</param>
    /// <param name="policy">The immutable policy matching the existing build.</param>
    /// <param name="resources">Optional explicitly borrowed legacy resources, never used as missing new artifacts.</param>
    /// <param name="resourceOwner">The owner transferred only on successful construction.</param>
    /// <param name="validateResources">The runtime source-generation validator.</param>
    /// <param name="retentionOptions">Optional retention policy, which must equal the immutable pipeline policy.</param>
    public Renderer2D(IGraphicDevice device, IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline, CommandRoutes2D routes, PipelinePolicy2D policy, DrawingResources2D? resources = null, IDisposable? resourceOwner = null, Action? validateResources = null, DrawingRetentionOptions2D? retentionOptions = null)
        : this(new PipelineBinding2D(device, pipeline, routes, policy), device, resources, resourceOwner, validateResources, retentionOptions)
    {
    }

    private Renderer2D(PipelineBinding2D binding, IGraphicDevice device, DrawingResources2D? resources, IDisposable? resourceOwner, Action? validateResources, DrawingRetentionOptions2D? retentionOptions)
        : this(device, binding.Renderers, resources, null, null, ValidateRetention(binding, retentionOptions))
    {
        _pipelineBinding = binding;
        _resources = new DrawingResourceState2D(device, resources, resourceOwner, validateResources, binding.Policy.Retention.MaxCachedBytes, pipelineMode: true);
        ArgumentTableMode = binding.Policy.ArgumentTables;

        // This is the last fallible operation. Ownership is attached only after all immutable validation succeeds.
        IDisposable use = binding.AcquireUse();
        _resources.AttachCompositionOwner(use);
    }

    internal PipelineBinding2D? PipelineBinding => _pipelineBinding;

    /// <summary>Prepares the first CPU interval and immutable execution ownership without recording or submitting.</summary>
    /// <param name="list">The contiguous immutable logical drawing.</param>
    /// <param name="target">The caller-owned matching native target.</param>
    /// <param name="cancellationToken">Cooperative preparation cancellation.</param>
    /// <returns>The shared generic segmented execution owner.</returns>
    public async ValueTask<PreparedPipelineExecution2D> PrepareExecutionAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(target.View);
        PipelineBinding2D binding = _pipelineBinding ?? throw new NotSupportedException("The legacy command table has no generic segmented execution contract.");
        binding.ValidateCommands(list.Commands);
        ValidateView(target.View);
        var info = new DrawingTargetInfo2D { Description = Describe(target.View.Texture), PixelScale = target.PixelScale, LoadOp = AttachmentLoadOp.Load };
        ValidateTarget(info with { LoadOp = target.LoadOp, ClearColor = target.ClearColor });
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation using one renderer is not supported.");
        }

        try
        {
            using IDisposable serial = await binding.AcquirePreparationAsync(cancellationToken).ConfigureAwait(false);
            var arguments = new DrawingArgumentPlan2D(_device) { ArgumentTableMode = ArgumentTableMode };
            using IDisposable argumentOwner = _resources.CleanupOwner(arguments);
            var context = new RenderPreparationContext2D(_device, info, _resources, arguments)
            {
                MaxWorkingBytes = _retained.MaxWorkingBytes,
                OtherWorkingBytes = arguments.EstimatedBytes,
                EnforceWorkingBudget = true,
                SourceBinding = new RenderSourceBinding2D(_device, target.View, binding.RecordContext),
            };
            PreparedPipelineExecution2D? execution = null;
            try
            {
                execution = await binding.PrepareExecutionAsync(context, list.Commands, target, cancellationToken).ConfigureAwait(false);
                await arguments.SealAsync(cancellationToken).ConfigureAwait(false);
                argumentOwner.Dispose();
                return execution;
            }
            catch (Exception error)
            {
                if (execution is not null)
                {
                    context.CleanupAfterFailure(execution, error);
                }

                throw;
            }
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }

    private static DrawingRetentionOptions2D ValidateRetention(PipelineBinding2D binding, DrawingRetentionOptions2D? options)
    {
        if (options is not null && options != binding.Policy.Retention)
        {
            throw new ArgumentException("Retention options cannot rewrite a compiled pipeline policy.", nameof(options));
        }

        return binding.Policy.Retention;
    }

    private static IReadOnlyList<DrawingCommand2D> TakePipelineCommands(DrawList2D list, ref int index, int end)
    {
        if (index != 0 || end != list.Commands.Count)
        {
            throw new InvalidOperationException("Direct pipeline preparation consumes one complete logical sequence.");
        }

        index = end;
        return list.Commands;
    }
}
