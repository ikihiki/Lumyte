using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares strategy-independent drawing without submitting or waiting for GPU work.</summary>
public interface IRenderer2D : IDisposable
{
    /// <summary>Gets the borrowed device, which is never disposed by this renderer.</summary>
    IGraphicDevice Device { get; }

    /// <summary>Gets issued preparation, reuse and owned-memory statistics, not driver-wide counters.</summary>
    DrawingRetentionStatistics2D RetentionStatistics { get; }

    /// <summary>Prepares a one-shot recording whose owner must survive GPU completion.</summary>
    /// <param name="list">The immutable drawing content.</param>
    /// <param name="target">The borrowed attachment.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>The prepared owner; recording does not publish GPU residency.</returns>
    ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default);

    /// <summary>Prepares retained content, borrowing the read lease only until completion of this call.</summary>
    /// <param name="source">The caller-owned live read lease.</param>
    /// <param name="target">The borrowed attachment.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>A frame requiring submission attachment and completion-safe retirement.</returns>
    ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default);

    /// <summary>Prepares retained immutable content without submitting work.</summary>
    /// <param name="source">The caller-owned snapshot, kept alive until the call completes.</param>
    /// <param name="target">The borrowed attachment.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>A frame requiring submission attachment and completion-safe retirement.</returns>
    ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default);
}
