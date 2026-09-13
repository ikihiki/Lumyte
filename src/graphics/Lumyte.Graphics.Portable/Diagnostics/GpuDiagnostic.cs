namespace Lumyte.Graphics.Portable;

/// <summary>A diagnostic reported by the runtime; no shader or resource validation is inferred by the wrapper.</summary>
public readonly record struct GpuDiagnostic(GpuDiagnosticKind Kind, string Message);

public enum GpuDiagnosticKind
{
    Validation,
    OutOfMemory,
    Internal,
    Runtime,
}
