namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a color render scope with actual texture views.</summary>
public sealed record RenderPassDesc
{
    /// <summary>Gets an optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the ordered color attachments.</summary>
    public required IReadOnlyList<RenderColorAttachmentDesc> ColorAttachments { get; init; }
}
