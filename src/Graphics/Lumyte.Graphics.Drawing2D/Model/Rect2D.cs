namespace Lumyte.Graphics.Drawing2D;

/// <summary>A rectangle in logical coordinates; drawing inputs require finite components and nonnegative dimensions.</summary>
/// <param name="X">The left coordinate.</param>
/// <param name="Y">The top coordinate.</param>
/// <param name="Width">The nonnegative width.</param>
/// <param name="Height">The nonnegative height.</param>
public readonly record struct Rect2D(float X, float Y, float Width, float Height);
