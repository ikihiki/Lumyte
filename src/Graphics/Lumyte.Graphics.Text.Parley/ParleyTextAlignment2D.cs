namespace Lumyte.Graphics.Text.Parley;

/// <summary>Aligns shaped lines inside the requested wrapping width.</summary>
public enum ParleyTextAlignment2D
{
    /// <summary>Aligns to the leading edge for the paragraph direction.</summary>
    Start,

    /// <summary>Aligns to the trailing edge for the paragraph direction.</summary>
    End,

    /// <summary>Centers each line.</summary>
    Center,

    /// <summary>Distributes eligible whitespace across the line.</summary>
    Justify,
}
