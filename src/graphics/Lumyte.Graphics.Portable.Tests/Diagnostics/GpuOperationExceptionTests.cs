namespace Lumyte.Graphics.Portable.Tests.Diagnostics;

public sealed class GpuOperationExceptionTests
{
    [Fact]
    public void DiagnosticsRemainStableWhenTheSourceCollectionChanges()
    {
        var validation = new GpuDiagnostic(GpuDiagnosticKind.Validation, "The runtime rejected this usage.");
        var runtime = new GpuDiagnostic(GpuDiagnosticKind.Runtime, "The resource operation did not complete.");
        List<GpuDiagnostic> source = [validation, runtime];
        var exception = new GpuOperationException("CreateBuffer", source);

        source[0] = new(GpuDiagnosticKind.Internal, "Replacement");
        source.Clear();

        Assert.Collection(exception.Diagnostics,
            diagnostic => Assert.Equal(validation, diagnostic),
            diagnostic => Assert.Equal(runtime, diagnostic));
    }

    [Fact]
    public void DiagnosticSequenceIsConsumedOnce()
    {
        int enumerations = 0;
        var diagnostic = new GpuDiagnostic(GpuDiagnosticKind.OutOfMemory, "Allocation failed.");
        IEnumerable<GpuDiagnostic> Diagnostics()
        {
            enumerations++;
            if (enumerations != 1) { throw new InvalidOperationException("Diagnostics were enumerated twice."); }
            yield return diagnostic;
        }

        var exception = new GpuOperationException("CreateTexture", Diagnostics());

        Assert.Equal(diagnostic, Assert.Single(exception.Diagnostics));
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void FailureIdentifiesTheOperationAndRuntimeDiagnostic()
    {
        const string message = "Requested buffer could not be mapped.";

        var exception = new GpuOperationException("MapBufferAsync", [new(GpuDiagnosticKind.Validation, message)]);

        Assert.Equal("MapBufferAsync", exception.Operation);
        Assert.Contains(exception.Operation, exception.Message);
        Assert.Contains(message, exception.Message);
    }
}
