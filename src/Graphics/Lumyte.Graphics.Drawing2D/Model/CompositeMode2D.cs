namespace Lumyte.Graphics.Drawing2D;

/// <summary>Selects an isolated COLRv1 compositing or blending operation.</summary>
public enum CompositeMode2D
{
    /// <summary>Uses Clear behavior.</summary>
    Clear,

    /// <summary>Uses Src behavior.</summary>
    Src,

    /// <summary>Uses Dest behavior.</summary>
    Dest,

    /// <summary>Uses SrcOver behavior.</summary>
    SrcOver,

    /// <summary>Uses DestOver behavior.</summary>
    DestOver,

    /// <summary>Uses SrcIn behavior.</summary>
    SrcIn,

    /// <summary>Uses DestIn behavior.</summary>
    DestIn,

    /// <summary>Uses SrcOut behavior.</summary>
    SrcOut,

    /// <summary>Uses DestOut behavior.</summary>
    DestOut,

    /// <summary>Uses SrcAtop behavior.</summary>
    SrcAtop,

    /// <summary>Uses DestAtop behavior.</summary>
    DestAtop,

    /// <summary>Uses Xor behavior.</summary>
    Xor,

    /// <summary>Uses Plus behavior.</summary>
    Plus,

    /// <summary>Uses Screen behavior.</summary>
    Screen,

    /// <summary>Uses Overlay behavior.</summary>
    Overlay,

    /// <summary>Uses Darken behavior.</summary>
    Darken,

    /// <summary>Uses Lighten behavior.</summary>
    Lighten,

    /// <summary>Uses ColorDodge behavior.</summary>
    ColorDodge,

    /// <summary>Uses ColorBurn behavior.</summary>
    ColorBurn,

    /// <summary>Uses HardLight behavior.</summary>
    HardLight,

    /// <summary>Uses SoftLight behavior.</summary>
    SoftLight,

    /// <summary>Uses Difference behavior.</summary>
    Difference,

    /// <summary>Uses Exclusion behavior.</summary>
    Exclusion,

    /// <summary>Uses Multiply behavior.</summary>
    Multiply,

    /// <summary>Uses HslHue behavior.</summary>
    HslHue,

    /// <summary>Uses HslSaturation behavior.</summary>
    HslSaturation,

    /// <summary>Uses HslColor behavior.</summary>
    HslColor,

    /// <summary>Uses HslLuminosity behavior.</summary>
    HslLuminosity,
}
