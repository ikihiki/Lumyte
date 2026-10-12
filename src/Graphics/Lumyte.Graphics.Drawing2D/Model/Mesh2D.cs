namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable triangle mesh with caller-supplied antialias coverage.</summary>
public sealed class Mesh2D
{
    /// <summary>Initializes a new instance of the <see cref="Mesh2D"/> class by validating and copying both inputs.</summary>
    /// <param name="vertices">Finite positions and coverage between zero and one.</param>
    /// <param name="indices">Triangle indices, each referring to an input vertex.</param>
    public Mesh2D(ReadOnlySpan<MeshVertex2D> vertices, ReadOnlySpan<uint> indices)
    {
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("The index count must be a multiple of three.", nameof(indices));
        }

        foreach (MeshVertex2D vertex in vertices)
        {
            ModelValidation2D.Vector(vertex.Position, nameof(vertices));
            ModelValidation2D.Unit(vertex.Coverage, nameof(vertices));
        }

        foreach (uint index in indices)
        {
            if (index >= vertices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(indices), "An index exceeds the vertex range.");
            }
        }

        Vertices = Array.AsReadOnly(vertices.ToArray());
        Indices = Array.AsReadOnly(indices.ToArray());
    }

    /// <summary>Gets the immutable vertex snapshot.</summary>
    public IReadOnlyList<MeshVertex2D> Vertices { get; }

    /// <summary>Gets the immutable triangle index snapshot.</summary>
    public IReadOnlyList<uint> Indices { get; }
}
