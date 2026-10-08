namespace Lumyte.Graphics;

/// <summary>
/// Specifies minification or magnification filtering.
/// </summary>
public enum FilterMode
{
    /// <summary>
    /// Chooses the nearest texel.
    /// </summary>
    Nearest,

    /// <summary>
    /// Interpolates neighboring texels.
    /// </summary>
    Linear,
}
