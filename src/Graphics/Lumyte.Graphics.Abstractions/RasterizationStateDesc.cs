namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes rasterization without vertex input layouts.</summary>
public sealed record RasterizationStateDesc
{
    /// <summary>Gets the discarded face selection.</summary>
    public CullMode Cull { get; init; } = CullMode.None;

    /// <summary>Gets the front face winding.</summary>
    public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;

    /// <summary>Gets a value indicating whether depth clipping is enabled.</summary>
    public bool DepthClipEnable { get; init; } = true;

    /// <summary>Gets the integer bias, within plus or minus 16777216.</summary>
    public int DepthBiasConstant { get; init; }

    /// <summary>Gets the finite slope bias.</summary>
    public float DepthBiasSlope { get; init; }

    /// <summary>Gets the finite optional bias clamp.</summary>
    public float DepthBiasClamp { get; init; }
}
