using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ui text layout.</summary>
public abstract class UiTextLayout
{
    /// <summary>Gets the document revision.</summary>
    public abstract ulong DocumentRevision { get; }

    /// <summary>Gets the size.</summary>
    public abstract Vector2 Size { get; }

    /// <summary>Gets the drawing.</summary>
    public abstract TextLayout2D Drawing { get; }

    /// <summary>Gets the caret stops.</summary>
    public abstract IReadOnlyList<UiTextCaret> CaretStops { get; }

    /// <summary>Get Caret Bounds.</summary>
    /// <param name = "caret">The caret.</param>
    /// <returns>The operation result.</returns>
    public abstract Rect2D GetCaretBounds(UiTextCaret caret);

    /// <summary>Get Range Bounds.</summary>
    /// <param name = "range">The range.</param>
    /// <returns>The operation result.</returns>
    public abstract IReadOnlyList<Rect2D> GetRangeBounds(TextSpan range);

    /// <summary>Hit Test.</summary>
    /// <param name = "position">The position.</param>
    /// <returns>The operation result.</returns>
    public abstract UiTextCaret HitTest(Vector2 position);

    /// <summary>Move Caret.</summary>
    /// <param name = "caret">The caret.</param>
    /// <param name = "forward">The forward.</param>
    /// <returns>The operation result.</returns>
    public abstract UiTextCaret MoveCaret(UiTextCaret caret, bool forward);
}
