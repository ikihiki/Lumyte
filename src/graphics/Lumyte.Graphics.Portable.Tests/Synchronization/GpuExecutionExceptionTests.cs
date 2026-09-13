namespace Lumyte.Graphics.Portable.Tests.Synchronization;

public sealed class GpuExecutionExceptionTests
{
    [Fact]
    public void FailureRetainsItsTimelineIdentityAndCopiesDiagnostics()
    {
        using var semaphore = new ExternalSemaphore();
        var point = new GpuFenceValue(semaphore, (1ul << 40) + 7);
        var diagnostic = new GpuDiagnostic(GpuDiagnosticKind.Validation, "Binding 3 is missing.");
        List<GpuDiagnostic> diagnostics = [diagnostic];

        var failure = new GpuExecutionException(point, diagnostics);
        diagnostics.Clear();

        Assert.Equal(point, failure.FenceValue);
        Assert.Equal(diagnostic, Assert.Single(failure.Diagnostics));
        Assert.Contains(diagnostic.Message, failure.Message);
    }

    [Fact]
    public void EqualNumbersFromDifferentTimelinesAreDifferentCompletionPoints()
    {
        using var first = new ExternalSemaphore();
        using var second = new ExternalSemaphore();

        Assert.NotEqual(new GpuFenceValue(first, 7), new GpuFenceValue(second, 7));
    }

    private sealed class ExternalSemaphore : GpuSemaphore
    {
        public override void Dispose() { }
    }
}
