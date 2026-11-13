using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application-owned material values and independent resource references.</summary>
public sealed partial record class BindingMaterial : IShaderData
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="BindingMaterial"/> class.</summary>
    /// <param name="color">The material multiplier.</param>
    /// <param name="texture">The sampled texture.</param>
    /// <param name="sampler">The independently registered sampler.</param>
    public BindingMaterial(Vector4 color, IGpuRef<IGraphicsTextureView> texture, IGpuRef<IGraphicsSampler> sampler)
    {
        _data = new()
        {
            Color = color,
        };

        Texture = texture;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the shader Color value.</summary>
    public partial Vector4 Color { get; set; }

    /// <summary>Gets or sets the shader Texture value.</summary>
    public IGpuRef<IGraphicsTextureView> Texture { get; set; }

    /// <summary>Gets or sets the shader Sampler value.</summary>
    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Vector4 Color;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}
