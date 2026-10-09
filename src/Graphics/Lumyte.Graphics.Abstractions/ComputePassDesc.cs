namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a compute recording scope.</summary>
public sealed record ComputePassDesc
{
    /// <summary>Gets an optional diagnostic label.</summary>
    public string? Label { get; init; }
}
