namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>A deterministic operation with no domain cost, measuring dispatch itself.</summary>
public sealed partial class OperationFixture
{
    /// <summary>Returns a typed result for the direct baseline.</summary>
    /// <param name="value">The scalar input.</param>
    /// <returns>The typed result.</returns>
    [DiagnosticOperation(DiagnosticPermission.Observe)]
    public DiagnosticResult<OperationReceipt> Echo(long value) => DiagnosticResult<OperationReceipt>.Success(new(value));
}
