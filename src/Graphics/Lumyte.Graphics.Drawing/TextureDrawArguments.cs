using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines root values for textured drawing.</summary>
public partial record struct TextureDrawArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="TextureDrawArguments"/> structure.</summary>
    /// <param name="vertices">The vertex range read using SV_VertexID.</param>
    /// <param name="transform">The object-to-clip matrix used with row vectors.</param>
    /// <param name="tint">The linear, straight-alpha color multiplier.</param>
    /// <param name="texture">The sampled two-dimensional texture.</param>
    /// <param name="sampler">The non-comparison texture sampler.</param>
    public TextureDrawArguments(IGpuRef<DrawingVertex> vertices, Matrix4x4 transform, Vector4 tint, IGpuRef<IGraphicsTextureView> texture, IGpuRef<IGraphicsSampler> sampler)
    {
        _data = new()
        {
            Transform = transform,
            Tint = tint,
        };

        Vertices = vertices;
        Texture = texture;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the shader Vertices value.</summary>
    public IGpuRef<DrawingVertex> Vertices { get; set; }

    /// <summary>Gets or sets the shader Transform value.</summary>
    public partial Matrix4x4 Transform { get; set; }

    /// <summary>Gets or sets the shader Tint value.</summary>
    public partial Vector4 Tint { get; set; }

    /// <summary>Gets or sets the shader Texture value.</summary>
    public IGpuRef<IGraphicsTextureView> Texture { get; set; }

    /// <summary>Gets or sets the shader Sampler value.</summary>
    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Vertices;
        public Matrix4x4 Transform;
        public Vector4 Tint;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}
