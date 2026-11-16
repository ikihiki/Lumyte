namespace Lumyte.Graphics.Drawing2D;

/// <summary>Prepares descriptor-only pass plans for the friend graph adapter.</summary>
internal interface IDrawingPassRenderer2D
{
    /// <summary>Prepares immutable content and fixed resource owners.</summary>
    /// <param name="content">The immutable recording.</param>
    /// <param name="target">The descriptor and pixel grid.</param>
    /// <param name="argumentPlan">The caller-owned pass resource allocator.</param>
    /// <param name="cancellationToken">The preparation cancellation.</param>
    /// <returns>The owned pass plan.</returns>
    ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawList2D content, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken);

    /// <summary>Prepares guarded scene content without retaining the guard.</summary>
    /// <param name="source">The live read lease.</param>
    /// <param name="target">The descriptor and pixel grid.</param>
    /// <param name="argumentPlan">The caller-owned pass resource allocator.</param>
    /// <param name="cancellationToken">The preparation cancellation.</param>
    /// <returns>The owned pass plan.</returns>
    ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSceneReadLease2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken);

    /// <summary>Prepares frozen scene content without retaining the source.</summary>
    /// <param name="source">The immutable scene snapshot.</param>
    /// <param name="target">The descriptor and pixel grid.</param>
    /// <param name="argumentPlan">The caller-owned pass resource allocator.</param>
    /// <param name="cancellationToken">The preparation cancellation.</param>
    /// <returns>The owned pass plan.</returns>
    ValueTask<DrawingPassPlan2D> PreparePassesAsync(DrawingSnapshot2D source, DrawingTargetInfo2D target, DrawingArgumentPlan2D argumentPlan, CancellationToken cancellationToken);
}
