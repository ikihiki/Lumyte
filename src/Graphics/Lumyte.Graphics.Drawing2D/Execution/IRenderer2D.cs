using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares direct or retained drawing without exposing internal stage execution.</summary>
public interface IRenderer2D : IDisposable
{
    /// <summary>Gets the borrowed generation-specific device.</summary>
    IGraphicDevice Device { get; }

    /// <summary>Gets cumulative renderer-local retained statistics.</summary>
    DrawingRetentionStatistics2D RetentionStatistics { get; }

    /// <summary>Prepares one immutable list; never submits.</summary>
    /// <param name="list">The input list.</param>
    /// <param name="target">The borrowed target.</param>
    /// <param name="cancellationToken">Preparation cancellation, not GPU cancellation.</param>
    /// <returns>The existing direct prepared owner.</returns>
    ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default);

    /// <summary>Borrows the read guard through completion of preparation.</summary>
    /// <param name="source">The live caller-owned guard.</param>
    /// <param name="target">The borrowed target.</param>
    /// <param name="cancellationToken">Preparation cancellation.</param>
    /// <returns>The existing completion-safe frame.</returns>
    ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default);

    /// <summary>Borrows the snapshot through completion of preparation.</summary>
    /// <param name="source">The immutable caller-owned snapshot.</param>
    /// <param name="target">The borrowed target.</param>
    /// <param name="cancellationToken">Preparation cancellation.</param>
    /// <returns>The existing completion-safe frame.</returns>
    ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default);
}
