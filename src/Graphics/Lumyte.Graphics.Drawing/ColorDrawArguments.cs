using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines root values for vertex-color drawing.</summary>
public partial record struct ColorDrawArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="ColorDrawArguments"/> structure.</summary>
    /// <param name="vertices">The vertex range read using SV_VertexID.</param>
    /// <param name="transform">The object-to-clip matrix used with row vectors.</param>
    /// <param name="tint">The linear, straight-alpha color multiplier.</param>
    public ColorDrawArguments(IGpuRef<DrawingVertex> vertices, Matrix4x4 transform, Vector4 tint)
    {
        _data = new()
        {
            Transform = transform,
            Tint = tint,
        };

        Vertices = vertices;
    }

    /// <summary>Gets or sets the shader Vertices value.</summary>
    public IGpuRef<DrawingVertex> Vertices { get; set; }

    /// <summary>Gets or sets the shader Transform value.</summary>
    public partial Matrix4x4 Transform { get; set; }

    /// <summary>Gets or sets the shader Tint value.</summary>
    public partial Vector4 Tint { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Vertices;
        public Matrix4x4 Transform;
        public Vector4 Tint;
    }
}
