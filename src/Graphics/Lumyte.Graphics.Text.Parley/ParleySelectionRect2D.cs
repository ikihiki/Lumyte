using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley;

/// <summary>Defines parley selection rect2 d.</summary>
/// <param name = "Start">The start.</param>
/// <param name = "Length">The length.</param>
/// <param name = "Bounds">The bounds.</param>
public sealed record ParleySelectionRect2D(int Start, int Length, Rect2D Bounds);
