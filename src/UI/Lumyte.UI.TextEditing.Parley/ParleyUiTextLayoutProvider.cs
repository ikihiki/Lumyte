using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing.Parley;

/// <summary>Provides copied visual caret and selection geometry from a borrowed Parley engine.</summary>
public sealed class ParleyUiTextLayoutProvider : IUiTextLayoutProvider
{
    private readonly ParleyTextEngine2D _engine;
    private readonly ParleyTextOptions2D _options;

    /// <summary>Initializes a new instance of the <see cref = "ParleyUiTextLayoutProvider"/> class.</summary>
    /// <param name = "engine">The engine.</param>
    /// <param name = "options">The options.</param>
    public ParleyUiTextLayoutProvider(ParleyTextEngine2D engine, ParleyTextOptions2D? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        options ??= new();
        _options = options with
        {
            IncludeEditingGeometry = true,
            FallbackFontFamilies = options.FallbackFontFamilies.ToArray(),
            FontFeatures = options.FontFeatures.ToArray(),
            FontVariations = options.FontVariations.ToArray(),
            TextRuns = options.TextRuns.ToArray(),
        };
    }

    /// <summary>Create Layout.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "maximumWidth">The maximum width.</param>
    /// <returns>The operation result.</returns>
    public UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (float.IsNaN(maximumWidth) || maximumWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        return new Layout(document, _engine.CreateDetailedLayout(document.Text, _options with { WrappingLength = maximumWidth == 0 ? float.PositiveInfinity : maximumWidth, }));
    }

    private sealed class Layout : UiTextLayout
    {
        private readonly ParleyTextLayout2D _layout;
        private readonly IReadOnlyDictionary<UiTextCaret, ParleyCaret2D> _carets;
        private readonly ParleySelectionRect2D[] _ranges;
        private readonly int[] _maximumEnds;

        /// <summary>Initializes a new instance of the <see cref = "Layout"/> class.</summary>
        /// <param name = "document">The document.</param>
        /// <param name = "layout">The layout.</param>
        public Layout(TextDocumentSnapshot document, ParleyTextLayout2D layout)
        {
            DocumentRevision = document.Revision;
            _layout = layout;
            _ranges = layout.SelectionRects.OrderBy(rect => rect.Start).ToArray();
            _maximumEnds = new int[_ranges.Length];
            for (int index = 0; index < _ranges.Length; index++)
            {
                _maximumEnds[index] = Math.Max(index == 0 ? 0 : _maximumEnds[index - 1], _ranges[index].Start + _ranges[index].Length);
            }

            HashSet<int> boundaries = [.. TextDocument.GraphemeBoundaries(document.Text)];
            _carets = layout.Carets.Where(c => boundaries.Contains(c.Position)).ToDictionary(c => new UiTextCaret(c.Position, c.Trailing ? TextCaretAffinity.Trailing : TextCaretAffinity.Leading));
            CaretStops = Array.AsReadOnly(_carets.Keys.OrderBy(c => _carets[c].Bounds.Y).ThenBy(c => _carets[c].Bounds.X).ToArray());
        }

        /// <summary>Gets the document revision.</summary>
        public override ulong DocumentRevision { get; }

        /// <summary>Gets the size.</summary>
        public override Vector2 Size => new(_layout.Layout.AdvanceBounds.Width, _layout.Layout.AdvanceBounds.Height);

        /// <summary>Gets the drawing.</summary>
        public override TextLayout2D Drawing => _layout.Layout;

        /// <summary>Gets the caret stops.</summary>
        public override IReadOnlyList<UiTextCaret> CaretStops { get; }

        /// <summary>Get Caret Bounds.</summary>
        /// <param name = "caret">The caret.</param>
        /// <returns>The operation result.</returns>
        public override Rect2D GetCaretBounds(UiTextCaret caret) => Find(caret).Bounds;

        /// <summary>Get Range Bounds.</summary>
        /// <param name = "range">The range.</param>
        /// <returns>The operation result.</returns>
        public override IReadOnlyList<Rect2D> GetRangeBounds(TextSpan range)
        {
            if (range.Length == 0)
            {
                return Array.Empty<Rect2D>();
            }

            int low = 0;
            int high = _ranges.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (_maximumEnds[middle] <= range.Start)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            var rectangles = new HashSet<Rect2D>();
            for (int index = low; index < _ranges.Length && _ranges[index].Start < range.Start + range.Length; index++)
            {
                ParleySelectionRect2D rect = _ranges[index];
                if (rect.Start + rect.Length > range.Start)
                {
                    rectangles.Add(rect.Bounds);
                }
            }

            return rectangles.ToArray();
        }

        /// <summary>Hit Test.</summary>
        /// <param name = "position">The position.</param>
        /// <returns>The operation result.</returns>
        public override UiTextCaret HitTest(Vector2 position)
        {
            if (_carets.Count == 0)
            {
                return default;
            }

            return _carets.MinBy(pair => MathF.Abs(pair.Value.Bounds.X - position.X) + (MathF.Abs(pair.Value.Bounds.Y + (pair.Value.Bounds.Height / 2) - position.Y) * 4)).Key;
        }

        /// <summary>Move Caret.</summary>
        /// <param name = "caret">The caret.</param>
        /// <param name = "forward">The forward.</param>
        /// <returns>The operation result.</returns>
        public override UiTextCaret MoveCaret(UiTextCaret caret, bool forward)
        {
            ParleyCaret2D current = Find(caret);
            var visited = new HashSet<UiTextCaret>();
            while (true)
            {
                UiTextCaret next = forward ? new UiTextCaret(current.NextPosition, current.NextTrailing ? TextCaretAffinity.Trailing : TextCaretAffinity.Leading) : new UiTextCaret(current.PreviousPosition, current.PreviousTrailing ? TextCaretAffinity.Trailing : TextCaretAffinity.Leading);
                if (_carets.ContainsKey(next))
                {
                    return next;
                }

                if (!visited.Add(next))
                {
                    return caret;
                }

                ParleyCaret2D? scalar = _layout.Carets.FirstOrDefault(c => c.Position == next.Position && c.Trailing == (next.Affinity == TextCaretAffinity.Trailing));
                if (scalar is null)
                {
                    return caret;
                }

                current = scalar;
            }
        }

        private ParleyCaret2D Find(UiTextCaret caret)
        {
            if (_carets.TryGetValue(caret, out ParleyCaret2D? found))
            {
                return found;
            }

            return _carets.FirstOrDefault(pair => pair.Key.Position == caret.Position).Value ?? throw new ArgumentOutOfRangeException(nameof(caret));
        }
    }
}
