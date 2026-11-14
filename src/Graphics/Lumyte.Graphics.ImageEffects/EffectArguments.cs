using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.ImageEffects;

internal partial record struct EffectArguments : IShaderArguments
{
    private TransferData _data;

    /// <summary>Initializes a new instance of the <see cref="EffectArguments"/> structure.</summary>
    public EffectArguments(IGpuRef<IGraphicsTextureView> input, IGpuRef<IGraphicsTextureView> backdrop, IGpuRef<IGraphicsSampler> sampler, Vector4 inputBounds, Vector4 backdropBounds, Vector4 outputBounds, Vector4 settings, Vector4 row0, Vector4 row1, Vector4 row2, Vector4 row3, Vector4 constant)
    {
        _data = new()
        {
            InputBounds = inputBounds,
            BackdropBounds = backdropBounds,
            OutputBounds = outputBounds,
            Settings = settings,
            Row0 = row0,
            Row1 = row1,
            Row2 = row2,
            Row3 = row3,
            Constant = constant,
        };

        Input = input;
        Backdrop = backdrop;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the shader Input value.</summary>
    public IGpuRef<IGraphicsTextureView> Input { get; set; }

    /// <summary>Gets or sets the shader Backdrop value.</summary>
    public IGpuRef<IGraphicsTextureView> Backdrop { get; set; }

    /// <summary>Gets or sets the shader Sampler value.</summary>
    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    /// <summary>Gets or sets the shader InputBounds value.</summary>
    public partial Vector4 InputBounds { get; set; }

    /// <summary>Gets or sets the shader BackdropBounds value.</summary>
    public partial Vector4 BackdropBounds { get; set; }

    /// <summary>Gets or sets the shader OutputBounds value.</summary>
    public partial Vector4 OutputBounds { get; set; }

    /// <summary>Gets or sets the shader Settings value.</summary>
    public partial Vector4 Settings { get; set; }

    /// <summary>Gets or sets the shader Row0 value.</summary>
    public partial Vector4 Row0 { get; set; }

    /// <summary>Gets or sets the shader Row1 value.</summary>
    public partial Vector4 Row1 { get; set; }

    /// <summary>Gets or sets the shader Row2 value.</summary>
    public partial Vector4 Row2 { get; set; }

    /// <summary>Gets or sets the shader Row3 value.</summary>
    public partial Vector4 Row3 { get; set; }

    /// <summary>Gets or sets the shader Constant value.</summary>
    public partial Vector4 Constant { get; set; }

    // This declaration is the wire ABI. Keep shader declarations in exactly this order.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Input;
        public ShaderReferenceSlot Backdrop;
        public ShaderReferenceSlot Sampler;
        public Vector4 InputBounds;
        public Vector4 BackdropBounds;
        public Vector4 OutputBounds;
        public Vector4 Settings;
        public Vector4 Row0;
        public Vector4 Row1;
        public Vector4 Row2;
        public Vector4 Row3;
        public Vector4 Constant;
    }
}
