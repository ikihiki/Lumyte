using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Holds borrowed, immutable converted CPU streams, with one vertex ABI and either native GPU index width.</summary>
/// <remarks>The producer must retain the backing memory for the lifetime of this value. No importer or conversion is performed.</remarks>
public sealed class ModelMeshData
{
    /// <summary>Initializes a new instance of the <see cref="ModelMeshData"/> class.</summary>
    /// <param name="vertices">Interleaved 64-byte vertices.</param>
    /// <param name="indices">Sixteen-bit triangle-list indices.</param>
    public ModelMeshData(ReadOnlyMemory<byte> vertices, ReadOnlyMemory<ushort> indices)
    {
        Vertices = vertices;
        Indices16 = indices;
        IndexFormat = IndexFormat.Uint16;
    }

    /// <summary>Initializes a new instance of the <see cref="ModelMeshData"/> class.</summary>
    /// <param name="vertices">Interleaved 64-byte vertices.</param>
    /// <param name="indices">Thirty-two-bit triangle-list indices.</param>
    public ModelMeshData(ReadOnlyMemory<byte> vertices, ReadOnlyMemory<uint> indices)
    {
        Vertices = vertices;
        Indices32 = indices;
        IndexFormat = IndexFormat.Uint32;
    }

    /// <summary>Gets the borrowed vertex bytes.</summary>
    public ReadOnlyMemory<byte> Vertices { get; }

    /// <summary>Gets the sixteen-bit indices, empty when IndexFormat is Uint32.</summary>
    public ReadOnlyMemory<ushort> Indices16 { get; }

    /// <summary>Gets the thirty-two-bit indices, empty when IndexFormat is Uint16.</summary>
    public ReadOnlyMemory<uint> Indices32 { get; }

    /// <summary>Gets the retained index width.</summary>
    public IndexFormat IndexFormat { get; }
}
