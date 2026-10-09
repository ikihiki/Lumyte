namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies how shaders interpret selected image layers.</summary>
public enum TextureViewDimension
{
    /// <summary>One two-dimensional layer.</summary>
    D2,

    /// <summary>A two-dimensional layer array.</summary>
    D2Array,

    /// <summary>Six square faces ordered +X, -X, +Y, -Y, +Z, -Z.</summary>
    Cube,

    /// <summary>One or more consecutive cubes with six layers each.</summary>
    CubeArray,
}
