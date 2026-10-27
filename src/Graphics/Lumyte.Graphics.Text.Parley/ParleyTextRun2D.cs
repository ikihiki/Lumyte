using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley;

/// <summary>A style override for a half-open UTF-16 range; boundaries must not split a surrogate pair.</summary>
public sealed record ParleyTextRun2D
{
    /// <summary>Gets the inclusive UTF-16 start index.</summary>
    public int Start { get; init; }

    /// <summary>Gets the exclusive UTF-16 end index.</summary>
    public int End { get; init; }

    /// <summary>Gets a registered font-family override, or null to inherit the paragraph font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Gets an optional positive finite em size.</summary>
    public float? FontSize { get; init; }

    /// <summary>Gets an optional font weight from one through one thousand.</summary>
    public float? FontWeight { get; init; }

    /// <summary>Gets an optional italic style override.</summary>
    public bool? Italic { get; init; }

    /// <summary>Gets an optional decoration override.</summary>
    public ParleyTextDecorations2D? Decorations { get; init; }

    /// <summary>Gets the paint in paragraph coordinates; null inherits the paragraph paint.</summary>
    public PaintGraph2D? Paint { get; init; }
}
