namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit command route identity; dispatch remains the renderer's fixed switch.</summary>
public enum DrawKind2D
{
    /// <summary>The FillRect command route.</summary>
    FillRect,

    /// <summary>The FillRoundedRect command route.</summary>
    FillRoundedRect,

    /// <summary>The FillCircle command route.</summary>
    FillCircle,

    /// <summary>The DrawLine command route.</summary>
    DrawLine,

    /// <summary>The FillPath command route.</summary>
    FillPath,

    /// <summary>The StrokePath command route.</summary>
    StrokePath,

    /// <summary>The DrawImage command route.</summary>
    DrawImage,

    /// <summary>The DrawCoverageMask command route.</summary>
    DrawCoverageMask,

    /// <summary>The DrawDistanceField command route.</summary>
    DrawDistanceField,

    /// <summary>The DrawMesh command route.</summary>
    DrawMesh,

    /// <summary>The DrawPaintGraph command route.</summary>
    DrawPaintGraph,
}
