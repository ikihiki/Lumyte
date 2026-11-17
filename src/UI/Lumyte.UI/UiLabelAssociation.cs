namespace Lumyte.UI;

/// <summary>Relates two ordinary direct children without inferring text or mutating a borrowed editor declaration.</summary>
public readonly record struct UiLabelAssociation
{
    /// <summary>Initializes a new instance of the <see cref="UiLabelAssociation"/> struct.</summary>
    /// <param name="labelChildIndex">The label's zero-based ordinary child position.</param>
    /// <param name="editorChildIndex">The editor's distinct zero-based ordinary child position.</param>
    /// <param name="accessibleName">An optional explicit application name, never inferred from visual content.</param>
    public UiLabelAssociation(int labelChildIndex, int editorChildIndex, string? accessibleName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(labelChildIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(editorChildIndex);
        if (labelChildIndex == editorChildIndex || (accessibleName is not null && string.IsNullOrWhiteSpace(accessibleName)))
        {
            throw new ArgumentException("Label and editor positions must differ, and an explicit name must not be empty.");
        }

        LabelChildIndex = labelChildIndex;
        EditorChildIndex = editorChildIndex;
        AccessibleName = accessibleName;
    }

    /// <summary>Gets the label's ordinary direct-child position.</summary>
    public int LabelChildIndex { get; }

    /// <summary>Gets the editor's ordinary direct-child position.</summary>
    public int EditorChildIndex { get; }

    /// <summary>Gets an optional explicit application name, not a name inferred from text.</summary>
    public string? AccessibleName { get; }
}
