namespace Lumyte.Graphics.Drawing2D;

/// <summary>Provides a guarded or frozen ordered CPU placement plan.</summary>
internal interface IDrawingSceneSource2D
{
    /// <summary>Gets the captured scene revision.</summary>
    ulong Revision { get; }

    /// <summary>Gets immutable content references and captured placement attributes.</summary>
    IReadOnlyList<DrawingSceneEntry2D> Entries { get; }
}
