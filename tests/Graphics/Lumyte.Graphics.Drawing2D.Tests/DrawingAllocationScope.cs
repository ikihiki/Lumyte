using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Isolates exact per-thread allocation counters from background-GC allocation-context invalidation.</summary>
/// <remarks>Use only in the nonparallel Drawing profiling collection. The reservation is not an allocation tolerance.</remarks>
internal sealed class DrawingAllocationScope : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="DrawingAllocationScope"/> class and asserts a bounded no-GC interval was obtained.</summary>
    internal DrawingAllocationScope()
    {
        const long Budget = 16 * 1024 * 1024;
        Assert.True(GC.TryStartNoGCRegion(Budget), "An exact allocation measurement requires a successfully reserved no-GC region.");
    }

    /// <inheritdoc/>
    public void Dispose() => GC.EndNoGCRegion();
}
