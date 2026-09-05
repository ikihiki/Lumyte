namespace Lumyte.Graphics.Text;

/// <summary>Selects the 2D route used to draw shaped glyphs.</summary>
public enum TextRenderingMode
{
    /// <summary>Select a route from the glyph's effective on-screen font size.</summary>
    Auto,

    /// <summary>Rasterize exact coverage at the selected physical size.</summary>
    Coverage,

    /// <summary>Use a single-channel signed-distance field.</summary>
    SignedDistance,

    /// <summary>Use a multi-channel signed-distance field.</summary>
    MultiChannelSignedDistance,

    /// <summary>Use reusable, pre-tessellated triangles when the outline supports it.</summary>
    Polygon,

    /// <summary>Submit the original glyph outline to the GPU path renderer.</summary>
    VectorPath,
}
