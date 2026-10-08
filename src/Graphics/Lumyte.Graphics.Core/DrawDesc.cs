namespace Lumyte.Graphics;
/// <summary>
/// Describes a non-indexed draw.
/// </summary>
public sealed record DrawDesc
{
    /// <summary>
    /// Gets the number of vertices per instance.
    /// </summary>
    public required uint VertexCount { get; init; }

    /// <summary>
    /// Gets the number of instances; defaults to one.
    /// </summary>
    public uint InstanceCount { get; init; } = 1;

    /// <summary>
    /// Gets the first vertex index; defaults to zero.
    /// </summary>
    public uint FirstVertex { get; init; }

    /// <summary>
    /// Gets the first instance index; defaults to zero.
    /// </summary>
    public uint FirstInstance { get; init; }
}
