namespace Lumyte.Graphics.Portable;

/// <summary>Runtime diagnostics for a device or resource operation, independently of a submitted batch.</summary>
public sealed class GpuOperationException : InvalidOperationException
{
    public GpuOperationException(string operation, IEnumerable<GpuDiagnostic> diagnostics)
        : this(operation, diagnostics?.ToArray() ?? throw new ArgumentNullException(nameof(diagnostics))) { }

    private GpuOperationException(string operation, GpuDiagnostic[] diagnostics)
        : base($"{operation} failed: {string.Join("; ", diagnostics.Select(item => item.Message))}")
    {
        Operation = operation;
        Diagnostics = Array.AsReadOnly(diagnostics);
    }

    public string Operation { get; }
    public IReadOnlyList<GpuDiagnostic> Diagnostics { get; }
}
