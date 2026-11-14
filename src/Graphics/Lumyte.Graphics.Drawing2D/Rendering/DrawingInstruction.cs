using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal partial record struct DrawingInstruction : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="DrawingInstruction"/> structure.</summary>
    public DrawingInstruction()
    {
        Texture = default!;
        Sampler = default!;
    }

    /// <summary>Initializes a new instance of the <see cref="DrawingInstruction"/> structure.</summary>
    public DrawingInstruction(Vector4 t0, Vector4 t1, Vector4 a, Vector4 b, Vector4 c, Vector4 d, uint kind, uint first, uint count, uint mode, IGpuRef<IGraphicsTextureView> texture, IGpuRef<IGraphicsSampler> sampler)
    {
        _data = new()
        {
            T0 = t0,
            T1 = t1,
            A = a,
            B = b,
            C = c,
            D = d,
            Kind = kind,
            First = first,
            Count = count,
            Mode = mode,
        };

        Texture = texture;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the shader T0 value.</summary>
    public partial Vector4 T0 { get; set; }

    /// <summary>Gets or sets the shader T1 value.</summary>
    public partial Vector4 T1 { get; set; }

    /// <summary>Gets or sets the shader A value.</summary>
    public partial Vector4 A { get; set; }

    /// <summary>Gets or sets the shader B value.</summary>
    public partial Vector4 B { get; set; }

    /// <summary>Gets or sets the shader C value.</summary>
    public partial Vector4 C { get; set; }

    /// <summary>Gets or sets the shader D value.</summary>
    public partial Vector4 D { get; set; }

    /// <summary>Gets or sets the shader Kind value.</summary>
    public partial uint Kind { get; set; }

    /// <summary>Gets or sets the shader First value.</summary>
    public partial uint First { get; set; }

    /// <summary>Gets or sets the shader Count value.</summary>
    public partial uint Count { get; set; }

    /// <summary>Gets or sets the shader Mode value.</summary>
    public partial uint Mode { get; set; }

    /// <summary>Gets or sets the shader Texture value.</summary>
    public IGpuRef<IGraphicsTextureView> Texture { get; set; }

    /// <summary>Gets or sets the shader Sampler value.</summary>
    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    internal int TextureIndex { get; init; } = -1;

    internal int SamplerIndex { get; init; } = -1;

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Vector4 T0;
        public Vector4 T1;
        public Vector4 A;
        public Vector4 B;
        public Vector4 C;
        public Vector4 D;
        public uint Kind;
        public uint First;
        public uint Count;
        public uint Mode;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}
