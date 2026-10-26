namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable drawing command snapshot.</summary>
/// <param name="State">The captured drawing state.</param>
public abstract record DrawingCommand2D(DrawingState2D State);
