namespace Lumyte.UI;

/// <summary>An immutable, detached preorder copy of the semantic tree.</summary>
public sealed class UiAutomationSnapshot
{
    internal UiAutomationSnapshot(Guid surfaceId, ulong sequence, UiAutomationNode[] nodes)
    {
        SurfaceId = surfaceId;
        Sequence = sequence;
        Nodes = Array.AsReadOnly(nodes);
    }

    /// <summary>Gets the surface identity.</summary>
    public Guid SurfaceId { get; }

    /// <summary>Gets the capture sequence, independent of rendered frames.</summary>
    public ulong Sequence { get; }

    /// <summary>Gets immutable nodes containing no widgets or callbacks.</summary>
    public IReadOnlyList<UiAutomationNode> Nodes { get; }
}
