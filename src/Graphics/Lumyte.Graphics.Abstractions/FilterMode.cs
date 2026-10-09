namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies spatial or mip-level filtering.</summary>
public enum FilterMode
{
    /// <summary>Selects the nearest sample.</summary>
    Nearest,

    /// <summary>Interpolates neighboring samples.</summary>
    Linear,
}
