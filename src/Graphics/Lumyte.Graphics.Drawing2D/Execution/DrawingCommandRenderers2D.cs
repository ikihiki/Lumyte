namespace Lumyte.Graphics.Drawing2D;

/// <summary>Stores the fixed, typed command rendering strategies selected at construction.</summary>
public sealed class DrawingCommandRenderers2D
{
    /// <summary>Initializes a new instance of the <see cref="DrawingCommandRenderers2D"/> class.</summary>
    /// <param name="fillRect">The FillRect renderer, borrowed by this table.</param>
    /// <param name="fillRoundedRect">The FillRoundedRect renderer, borrowed by this table.</param>
    /// <param name="fillCircle">The FillCircle renderer, borrowed by this table.</param>
    /// <param name="drawLine">The DrawLine renderer, borrowed by this table.</param>
    /// <param name="fillPath">The FillPath renderer, borrowed by this table.</param>
    /// <param name="strokePath">The StrokePath renderer, borrowed by this table.</param>
    /// <param name="drawImage">The DrawImage renderer, borrowed by this table.</param>
    /// <param name="drawCoverageMask">The DrawCoverageMask renderer, borrowed by this table.</param>
    /// <param name="drawDistanceField">The DrawDistanceField renderer, borrowed by this table.</param>
    /// <param name="drawMesh">The DrawMesh renderer, borrowed by this table.</param>
    /// <param name="drawPaintGraph">The DrawPaintGraph renderer, borrowed by this table.</param>
    public DrawingCommandRenderers2D(
        IFillRectRenderer2D fillRect,
        IFillRoundedRectRenderer2D fillRoundedRect,
        IFillCircleRenderer2D fillCircle,
        IDrawLineRenderer2D drawLine,
        IFillPathRenderer2D fillPath,
        IStrokePathRenderer2D strokePath,
        IDrawImageRenderer2D drawImage,
        IDrawCoverageMaskRenderer2D drawCoverageMask,
        IDrawDistanceFieldRenderer2D drawDistanceField,
        IDrawMeshRenderer2D drawMesh,
        IDrawPaintGraphRenderer2D drawPaintGraph)
    {
        ArgumentNullException.ThrowIfNull(fillRect);
        FillRect = fillRect;
        ArgumentNullException.ThrowIfNull(fillRoundedRect);
        FillRoundedRect = fillRoundedRect;
        ArgumentNullException.ThrowIfNull(fillCircle);
        FillCircle = fillCircle;
        ArgumentNullException.ThrowIfNull(drawLine);
        DrawLine = drawLine;
        ArgumentNullException.ThrowIfNull(fillPath);
        FillPath = fillPath;
        ArgumentNullException.ThrowIfNull(strokePath);
        StrokePath = strokePath;
        ArgumentNullException.ThrowIfNull(drawImage);
        DrawImage = drawImage;
        ArgumentNullException.ThrowIfNull(drawCoverageMask);
        DrawCoverageMask = drawCoverageMask;
        ArgumentNullException.ThrowIfNull(drawDistanceField);
        DrawDistanceField = drawDistanceField;
        ArgumentNullException.ThrowIfNull(drawMesh);
        DrawMesh = drawMesh;
        ArgumentNullException.ThrowIfNull(drawPaintGraph);
        DrawPaintGraph = drawPaintGraph;
    }

    /// <summary>Gets the configured FillRect renderer.</summary>
    public IFillRectRenderer2D FillRect { get; }

    /// <summary>Gets the configured FillRoundedRect renderer.</summary>
    public IFillRoundedRectRenderer2D FillRoundedRect { get; }

    /// <summary>Gets the configured FillCircle renderer.</summary>
    public IFillCircleRenderer2D FillCircle { get; }

    /// <summary>Gets the configured DrawLine renderer.</summary>
    public IDrawLineRenderer2D DrawLine { get; }

    /// <summary>Gets the configured FillPath renderer.</summary>
    public IFillPathRenderer2D FillPath { get; }

    /// <summary>Gets the configured StrokePath renderer.</summary>
    public IStrokePathRenderer2D StrokePath { get; }

    /// <summary>Gets the configured DrawImage renderer.</summary>
    public IDrawImageRenderer2D DrawImage { get; }

    /// <summary>Gets the configured DrawCoverageMask renderer.</summary>
    public IDrawCoverageMaskRenderer2D DrawCoverageMask { get; }

    /// <summary>Gets the configured DrawDistanceField renderer.</summary>
    public IDrawDistanceFieldRenderer2D DrawDistanceField { get; }

    /// <summary>Gets the configured DrawMesh renderer.</summary>
    public IDrawMeshRenderer2D DrawMesh { get; }

    /// <summary>Gets the configured DrawPaintGraph renderer.</summary>
    public IDrawPaintGraphRenderer2D DrawPaintGraph { get; }

    /// <summary>Creates a new table and 11 stateless defaults without DI or GPU allocations.</summary>
    /// <returns>The new rendering strategy table.</returns>
    public static DrawingCommandRenderers2D CreateDefault() => new(
        new DefaultFillRectRenderer2D(),
        new DefaultFillRoundedRectRenderer2D(),
        new DefaultFillCircleRenderer2D(),
        new DefaultDrawLineRenderer2D(),
        new DefaultFillPathRenderer2D(),
        new DefaultStrokePathRenderer2D(),
        new DefaultDrawImageRenderer2D(),
        new DefaultDrawCoverageMaskRenderer2D(),
        new DefaultDrawDistanceFieldRenderer2D(),
        new DefaultDrawMeshRenderer2D(),
        new DefaultDrawPaintGraphRenderer2D());
}
