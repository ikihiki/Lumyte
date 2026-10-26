namespace Lumyte.Graphics.Drawing2D;

/// <summary>Stores the fixed, typed command rendering strategies selected at construction.</summary>
public sealed class DrawingCommandRenderers2D
{
    /// <summary>Initializes a new instance of the <see cref="DrawingCommandRenderers2D"/> class.</summary>
    /// <param name="fillRect">The FillRect renderer, borrowed by this table.</param>
    /// <param name="fillRoundedRect">The FillRoundedRect renderer, borrowed by this table.</param>
    /// <param name="fillCircle">The FillCircle renderer, borrowed by this table.</param>
    public DrawingCommandRenderers2D(
        IFillRectRenderer2D fillRect,
        IFillRoundedRectRenderer2D fillRoundedRect,
        IFillCircleRenderer2D fillCircle)
    {
        ArgumentNullException.ThrowIfNull(fillRect);
        FillRect = fillRect;
        ArgumentNullException.ThrowIfNull(fillRoundedRect);
        FillRoundedRect = fillRoundedRect;
        ArgumentNullException.ThrowIfNull(fillCircle);
        FillCircle = fillCircle;
    }

    /// <summary>Gets the configured FillRect renderer.</summary>
    public IFillRectRenderer2D FillRect { get; }

    /// <summary>Gets the configured FillRoundedRect renderer.</summary>
    public IFillRoundedRectRenderer2D FillRoundedRect { get; }

    /// <summary>Gets the configured FillCircle renderer.</summary>
    public IFillCircleRenderer2D FillCircle { get; }

    /// <summary>Creates a new table and 3 stateless defaults without DI or GPU allocations.</summary>
    /// <returns>The new rendering strategy table.</returns>
    public static DrawingCommandRenderers2D CreateDefault() => new(
        new DefaultFillRectRenderer2D(),
        new DefaultFillRoundedRectRenderer2D(),
        new DefaultFillCircleRenderer2D());
}
