namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes immutable sampling state without owning GPU resources.</summary>
public sealed record SamplerDesc
{
    /// <summary>Gets minification filtering.</summary>
    public FilterMode MinFilter { get; init; } = FilterMode.Linear;

    /// <summary>Gets magnification filtering.</summary>
    public FilterMode MagFilter { get; init; } = FilterMode.Linear;

    /// <summary>Gets interpolation between mip levels.</summary>
    public FilterMode MipmapFilter { get; init; } = FilterMode.Linear;

    /// <summary>Gets addressing along the U coordinate.</summary>
    public AddressMode AddressU { get; init; } = AddressMode.Repeat;

    /// <summary>Gets addressing along the V coordinate.</summary>
    public AddressMode AddressV { get; init; } = AddressMode.Repeat;

    /// <summary>Gets addressing along the W coordinate.</summary>
    public AddressMode AddressW { get; init; } = AddressMode.Repeat;

    /// <summary>Gets the finite nonnegative minimum LOD.</summary>
    public float LodMinClamp { get; init; } = 0;

    /// <summary>Gets the finite maximum LOD, including zero when both clamps are zero.</summary>
    public float LodMaxClamp { get; init; } = 32;

    /// <summary>Gets the positive anisotropic filtering limit; values above one require linear filters.</summary>
    public ushort MaxAnisotropy { get; init; } = 1;

    /// <summary>Gets the comparison function, or null for ordinary sampling.</summary>
    public CompareFunction? Compare { get; init; } = null;
}
