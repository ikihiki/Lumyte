namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies how presentation alpha is composed.</summary>
public enum SurfaceAlphaMode
{
    /// <summary>Selects a backend-supported composition mode.</summary>
    Auto,

    /// <summary>Treats the presented image as opaque.</summary>
    Opaque,

    /// <summary>Uses colors already multiplied by their alpha.</summary>
    Premultiplied,
}
