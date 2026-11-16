namespace Lumyte.Mathematics.Colors;

/// <summary>Numeric component encoding, independent of channel layout and transfer function.</summary>
public enum ColorComponentEncoding
{
    /// <summary>Unsigned normalized 8-bit integer components.</summary>
    Unorm8,

    /// <summary>Unsigned normalized 16-bit integer components.</summary>
    Unorm16,

    /// <summary>Signed normalized 8-bit integer components.</summary>
    Snorm8,

    /// <summary>Signed normalized 16-bit integer components.</summary>
    Snorm16,

    /// <summary>Finite IEEE 16-bit floating-point components.</summary>
    Float16,

    /// <summary>Finite IEEE 32-bit floating-point components.</summary>
    Float32,

    /// <summary>Finite IEEE 64-bit floating-point components.</summary>
    Float64,
}
