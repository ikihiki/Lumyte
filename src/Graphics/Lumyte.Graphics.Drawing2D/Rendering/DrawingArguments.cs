using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed partial record class DrawingArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="DrawingArguments"/> class.</summary>
    public DrawingArguments(IGpuRef<DrawingInstruction> instructions, IGpuRef<Vector4> data, uint count, uint root, uint graphMode, float opacity, Vector4 sourceBounds, Vector4 originalBounds, Vector4 pixelOrigin, IGpuRef<IGraphicsTextureView> sourceTexture, IGpuRef<IGraphicsTextureView> originalTexture, IGpuRef<IGraphicsSampler> graphSampler)
    {
        _data = new()
        {
            Count = count,
            Root = root,
            GraphMode = graphMode,
            Opacity = opacity,
            SourceBounds = sourceBounds,
            OriginalBounds = originalBounds,
            PixelOrigin = pixelOrigin,
        };

        Instructions = instructions;
        Data = data;
        SourceTexture = sourceTexture;
        OriginalTexture = originalTexture;
        GraphSampler = graphSampler;
    }

    /// <summary>Gets or sets the shader Instructions value.</summary>
    public IGpuRef<DrawingInstruction> Instructions { get; set; }

    /// <summary>Gets or sets the shader Data value.</summary>
    public IGpuRef<Vector4> Data { get; set; }

    /// <summary>Gets or sets the shader Count value.</summary>
    public partial uint Count { get; set; }

    /// <summary>Gets or sets the shader Root value.</summary>
    public partial uint Root { get; set; }

    /// <summary>Gets or sets the shader GraphMode value.</summary>
    public partial uint GraphMode { get; set; }

    /// <summary>Gets or sets the shader Opacity value.</summary>
    public partial float Opacity { get; set; }

    /// <summary>Gets or sets the shader SourceBounds value.</summary>
    public partial Vector4 SourceBounds { get; set; }

    /// <summary>Gets or sets the shader OriginalBounds value.</summary>
    public partial Vector4 OriginalBounds { get; set; }

    /// <summary>Gets or sets the shader PixelOrigin value.</summary>
    public partial Vector4 PixelOrigin { get; set; }

    /// <summary>Gets or sets the shader SourceTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> SourceTexture { get; set; }

    /// <summary>Gets or sets the shader OriginalTexture value.</summary>
    public IGpuRef<IGraphicsTextureView> OriginalTexture { get; set; }

    /// <summary>Gets or sets the shader GraphSampler value.</summary>
    public IGpuRef<IGraphicsSampler> GraphSampler { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Instructions;
        public ShaderReferenceSlot Data;
        public uint Count;
        public uint Root;
        public uint GraphMode;
        public float Opacity;
        public Vector4 SourceBounds;
        public Vector4 OriginalBounds;
        public Vector4 PixelOrigin;
        public ShaderReferenceSlot SourceTexture;
        public ShaderReferenceSlot OriginalTexture;
        public ShaderReferenceSlot GraphSampler;
    }
}
