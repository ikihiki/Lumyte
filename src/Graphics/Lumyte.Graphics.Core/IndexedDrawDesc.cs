namespace Lumyte.Graphics;
/// <summary>
/// Describes a draw using the selected index buffer.
/// </summary>
public sealed record IndexedDrawDesc
{
    /// <summary>
    /// Gets the number of index elements per instance.
    /// </summary>
    public required uint IndexCount { get; init; }

    /// <summary>
    /// Gets the number of instances; defaults to one.
    /// </summary>
    public uint InstanceCount { get; init; } = 1;

    /// <summary>
    /// Gets the first element within the selected index range.
    /// </summary>
    public uint FirstIndex { get; init; }

    /// <summary>
    /// Gets the signed offset added to fetched vertex indices.
    /// </summary>
    public int BaseVertex { get; init; }

    /// <summary>
    /// Gets the first instance index; defaults to zero.
    /// </summary>
    public uint FirstInstance { get; init; }
}
