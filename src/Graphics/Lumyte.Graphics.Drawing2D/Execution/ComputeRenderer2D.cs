using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Generates curves, classifies tiles and composites correlated coverage and paint on the GPU.</summary>
/// <param name="device">The borrowed device.</param>
/// <param name="options">The positive working budget.</param>
/// <param name="retentionOptions">The retained frame and idle bounds.</param>
public sealed class ComputeRenderer2D(IGraphicDevice device, DrawingRendererOptions2D? options = null, DrawingRetentionOptions2D? retentionOptions = null) : IRenderer2D, IDrawingPassRenderer2D
{
    private readonly DrawingRendererOptions2D _options = (options ?? new()).Validate();
    private readonly DrawingResourceState2D _resources = new(device);
    private readonly RetainedDrawingCache2D _retained = new((retentionOptions ?? new()) with
    {
        MaxCachedBytes = (retentionOptions ?? new()).MaxCachedBytes - ((retentionOptions ?? new()).MaxCachedBytes / 4),
        MaxCachedAssets = (retentionOptions ?? new()).MaxCachedAssets - ((retentionOptions ?? new()).MaxCachedAssets / 2),
    });

    private readonly DrawingCpuShapeCache2D _cpuShapes = new((retentionOptions ?? new()).MaxCachedBytes / 4, (retentionOptions ?? new()).MaxCachedAssets / 2);

    private bool _disposed;
    private int _preparing;

    /// <inheritdoc/>
    public IGraphicDevice Device { get; } = device ?? throw new ArgumentNullException(nameof(device));

    /// <inheritdoc/>
    public DrawingRetentionStatistics2D RetentionStatistics
    {
        get
        {
            DrawingRetentionStatistics2D gpu = _retained.Statistics;
            return gpu with { CachedAssets = gpu.CachedAssets + _cpuShapes.AssetCount, CachedBytes = checked(gpu.CachedBytes + _cpuShapes.EstimatedBytes) };
        }
    }

    /// <inheritdoc/>
    public async ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IPreparedCommandBatch2D> batches = await PrepareDirectAsync(list, DrawingFramePreparation2D.Describe(target), cancellationToken).ConfigureAwait(false);
        return new(target, batches);
    }

    /// <inheritdoc/>
    public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DrawingFramePreparation2D.PrepareAsync(Device, source, target, _retained, PrepareDirectAsync, cancellationToken, _options.MaxWorkingBytes);
    }

    /// <inheritdoc/>
    public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DrawingFramePreparation2D.PrepareAsync(Device, source, target, _retained, PrepareDirectAsync, cancellationToken, _options.MaxWorkingBytes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        _cpuShapes.Clear();
        _retained.Dispose();
        _resources.Dispose();
    }

    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PreparePassesAsync(content, target, argumentPlan, cancellationToken);

    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawingSceneReadLease2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PreparePassesAsync(DrawingSceneCapture2D.Capture(source, cancellationToken, _options.MaxWorkingBytes), target, argumentPlan, cancellationToken);

    /// <inheritdoc/>
    ValueTask<DrawingPassPlan2D> IDrawingPassRenderer2D.PreparePassesAsync(DrawingSnapshot2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
        => PreparePassesAsync(DrawingSceneCapture2D.Capture(source, cancellationToken, _options.MaxWorkingBytes), target, argumentPlan, cancellationToken);

    private async ValueTask<IReadOnlyList<IPreparedCommandBatch2D>> PrepareDirectAsync(DrawList2D content, DrawingTargetInfo2D target, CancellationToken cancellationToken)
    {
        using var plan = new DrawingArgumentPlan2D(Device);
        ComputeExecution2D batch = await PrepareBatchAsync(content, target, plan, cancellationToken).ConfigureAwait(false);
        try
        {
            await plan.SealAsync(cancellationToken).ConfigureAwait(false);
            return [batch];
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    private async ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
    {
        ComputeExecution2D batch = await PrepareBatchAsync(content, target, argumentPlan, cancellationToken).ConfigureAwait(false);
        var result = new DrawingPassPlan2D(argumentPlan);
        result.Own(batch);
        foreach (DrawingPass2D pass in batch.Passes)
        {
            bool resolve = pass.Name.EndsWith(".resolve", StringComparison.Ordinal);
            result.Add(pass with { UsesTarget = resolve, LocalResources = true });
        }

        return result;
    }

    private async ValueTask<ComputeExecution2D> PrepareBatchAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);
        DrawingFramePreparation2D.Validate(target);
        DrawingWorkingBudget2D.Validate(content, target, _options.MaxWorkingBytes - content.CapturedWorkingBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(argumentPlan.Device, Device))
        {
            throw new ArgumentException("The argument plan belongs to another device.", nameof(argumentPlan));
        }

        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0)
        {
            throw new InvalidOperationException("Concurrent preparation on one renderer is unsupported.");
        }

        try
        {
            var context = new RenderPreparationContext2D(Device, target, _resources, argumentPlan) { MaxWorkingBytes = _options.MaxWorkingBytes - content.CapturedWorkingBytes, CpuShapes = _cpuShapes };
            return (ComputeExecution2D)await ComputeExecution2D.PrepareAsync(context, content.Commands.ToArray().AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }
}
