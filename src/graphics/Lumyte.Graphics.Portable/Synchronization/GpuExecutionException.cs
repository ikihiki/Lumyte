namespace Lumyte.Graphics.Portable;

/// <summary>A submission failed after its GPU use ended. Its output must not be treated as successful.</summary>
public sealed class GpuExecutionException : InvalidOperationException
{
    public GpuExecutionException(GpuFenceValue fenceValue, IReadOnlyList<GpuDiagnostic> diagnostics)
        : this(fenceValue, diagnostics?.ToArray() ?? throw new ArgumentNullException(nameof(diagnostics))) { }

    private GpuExecutionException(GpuFenceValue fenceValue, GpuDiagnostic[] diagnostics)
        : base($"GPU submission at timeline value {fenceValue.Value} failed: {string.Join("; ", diagnostics.Select(item => item.Message))}")
    {
        FenceValue = fenceValue;
        Diagnostics = Array.AsReadOnly(diagnostics);
    }

    public GpuFenceValue FenceValue { get; }
    public IReadOnlyList<GpuDiagnostic> Diagnostics { get; }
}
