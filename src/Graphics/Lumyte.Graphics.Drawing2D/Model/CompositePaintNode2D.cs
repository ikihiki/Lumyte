namespace Lumyte.Graphics.Drawing2D;

/// <summary>Composites two isolated paint subgraphs in linear-light premultiplied RGBA.</summary>
/// <param name="Source">The source paint.</param>
/// <param name="Backdrop">The isolated backdrop paint, independent of the render target.</param>
/// <param name="Mode">The OpenType COLR compositing operator.</param>
public sealed record CompositePaintNode2D(PaintNode2D Source, PaintNode2D Backdrop, CompositeMode2D Mode) : PaintNode2D;
