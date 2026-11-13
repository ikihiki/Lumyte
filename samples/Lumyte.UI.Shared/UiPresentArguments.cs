using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.UI.SampleSupport;

internal sealed partial record class UiPresentArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="UiPresentArguments"/> class.</summary>
    public UiPresentArguments(IGpuRef<IGraphicsTextureView> image, IGpuRef<IGraphicsSampler> sampler, Vector2 size)
    {
        _data = new()
        {
            Size = size,
        };

        Image = image;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the shader Image value.</summary>
    public IGpuRef<IGraphicsTextureView> Image { get; set; }

    /// <summary>Gets or sets the shader Sampler value.</summary>
    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    /// <summary>Gets or sets the shader Size value.</summary>
    public partial Vector2 Size { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Image;
        public ShaderReferenceSlot Sampler;
        public Vector2 Size;
    }
}
