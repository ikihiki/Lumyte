namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a one-shot command recording.</summary>
public sealed record CommandBufferDesc
{
    /// <summary>Gets an optional diagnostic label.</summary>
    public string? Label { get; init; }
}
