using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley;

/// <summary>Defines parley caret2 d.</summary>
/// <param name = "Position">The position.</param>
/// <param name = "Trailing">The trailing.</param>
/// <param name = "Bounds">The bounds.</param>
/// <param name = "PreviousPosition">The previous position.</param>
/// <param name = "PreviousTrailing">The previous trailing.</param>
/// <param name = "NextPosition">The next position.</param>
/// <param name = "NextTrailing">The next trailing.</param>
public sealed record ParleyCaret2D(int Position, bool Trailing, Rect2D Bounds, int PreviousPosition, bool PreviousTrailing, int NextPosition, bool NextTrailing);
