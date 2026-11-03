namespace Lumyte.Graphics.Drawing2D;

/// <summary>A reusable immutable CPU recording with typed command storage and a painter-order tape.</summary>
public sealed class DrawList2D
{
    private readonly int[] _typeOffsets = [];
    private readonly FillRectCommand2D[] _fillRect = [];
    private readonly FillRoundedRectCommand2D[] _fillRoundedRect = [];
    private readonly FillCircleCommand2D[] _fillCircle = [];
    private readonly DrawLineCommand2D[] _drawLine = [];
    private readonly FillPathCommand2D[] _fillPath = [];
    private readonly StrokePathCommand2D[] _strokePath = [];
    private readonly DrawImageCommand2D[] _drawImage = [];
    private readonly DrawCoverageMaskCommand2D[] _drawCoverageMask = [];
    private readonly DrawDistanceFieldCommand2D[] _drawDistanceField = [];
    private readonly DrawMeshCommand2D[] _drawMesh = [];
    private readonly DrawPaintGraphCommand2D[] _drawPaintGraph = [];

    private readonly IReadOnlyList<DrawingCommand2D>? _commands;
    private DrawingContentPages2D? _retainedPages;
    private DrawList2D? _materialized;

    internal DrawList2D(IEnumerable<DrawingCommand2D> commands)
    {
        DrawingCommand2D[] ordered = commands.ToArray();
        _commands = Array.AsReadOnly(ordered);
        Count = ordered.Length;
        _typeOffsets = new int[ordered.Length];
        var fillRect = new List<FillRectCommand2D>();
        var fillRoundedRect = new List<FillRoundedRectCommand2D>();
        var fillCircle = new List<FillCircleCommand2D>();
        var drawLine = new List<DrawLineCommand2D>();
        var fillPath = new List<FillPathCommand2D>();
        var strokePath = new List<StrokePathCommand2D>();
        var drawImage = new List<DrawImageCommand2D>();
        var drawCoverageMask = new List<DrawCoverageMaskCommand2D>();
        var drawDistanceField = new List<DrawDistanceFieldCommand2D>();
        var drawMesh = new List<DrawMeshCommand2D>();
        var drawPaintGraph = new List<DrawPaintGraphCommand2D>();
        for (int i = 0; i < ordered.Length; i++)
        {
            switch (ordered[i])
            {
                case FillRectCommand2D command:
                    _typeOffsets[i] = fillRect.Count;
                    fillRect.Add(command);
                    break;
                case FillRoundedRectCommand2D command:
                    _typeOffsets[i] = fillRoundedRect.Count;
                    fillRoundedRect.Add(command);
                    break;
                case FillCircleCommand2D command:
                    _typeOffsets[i] = fillCircle.Count;
                    fillCircle.Add(command);
                    break;
                case DrawLineCommand2D command:
                    _typeOffsets[i] = drawLine.Count;
                    drawLine.Add(command);
                    break;
                case FillPathCommand2D command:
                    _typeOffsets[i] = fillPath.Count;
                    fillPath.Add(command);
                    break;
                case StrokePathCommand2D command:
                    _typeOffsets[i] = strokePath.Count;
                    strokePath.Add(command);
                    break;
                case DrawImageCommand2D command:
                    _typeOffsets[i] = drawImage.Count;
                    drawImage.Add(command);
                    break;
                case DrawCoverageMaskCommand2D command:
                    _typeOffsets[i] = drawCoverageMask.Count;
                    drawCoverageMask.Add(command);
                    break;
                case DrawDistanceFieldCommand2D command:
                    _typeOffsets[i] = drawDistanceField.Count;
                    drawDistanceField.Add(command);
                    break;
                case DrawMeshCommand2D command:
                    _typeOffsets[i] = drawMesh.Count;
                    drawMesh.Add(command);
                    break;
                case DrawPaintGraphCommand2D command:
                    _typeOffsets[i] = drawPaintGraph.Count;
                    drawPaintGraph.Add(command);
                    break;
            }
        }

        _fillRect = fillRect.ToArray();
        _fillRoundedRect = fillRoundedRect.ToArray();
        _fillCircle = fillCircle.ToArray();
        _drawLine = drawLine.ToArray();
        _fillPath = fillPath.ToArray();
        _strokePath = strokePath.ToArray();
        _drawImage = drawImage.ToArray();
        _drawCoverageMask = drawCoverageMask.ToArray();
        _drawDistanceField = drawDistanceField.ToArray();
        _drawMesh = drawMesh.ToArray();
        _drawPaintGraph = drawPaintGraph.ToArray();
    }

    private DrawList2D(DrawingContentPages2D pages)
    {
        _retainedPages = pages;
        Count = pages.Count;
    }

    /// <summary>Gets the number of immutable drawing commands in painter order.</summary>
    public int Count { get; }

    internal bool HasRetainedPages => Volatile.Read(ref _retainedPages) is not null;

    internal IReadOnlyList<DrawingCommand2D> Commands => _commands ?? Materialize().Commands;

    internal DrawingContentPages2D RetainedPages
    {
        get
        {
            DrawingContentPages2D? pages = Volatile.Read(ref _retainedPages);
            if (pages is not null)
            {
                return pages;
            }

            pages = DrawingContentPages2D.FromDrawList(this);
            return Interlocked.CompareExchange(ref _retainedPages, pages, null) ?? pages;
        }
    }

    internal DrawList2D WithPage(int index, DrawList2D replacement)
    {
        DrawingContentPages2D original = RetainedPages;
        DrawingContentPages2D changed = original.WithPage(index, replacement);
        return ReferenceEquals(original, changed) ? this : new(changed);
    }

    internal ReadOnlyMemory<TCommand> GetCommands<TCommand>(int orderStart, int count)
        where TCommand : DrawingCommand2D
    {
        if (_commands is null)
        {
            return Materialize().GetCommands<TCommand>(orderStart, count);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(orderStart);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (orderStart > Commands.Count - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return ReadOnlyMemory<TCommand>.Empty;
        }

        if (Commands[orderStart] is not TCommand || Commands[orderStart + count - 1] is not TCommand || _typeOffsets[orderStart + count - 1] - _typeOffsets[orderStart] != count - 1)
        {
            throw new ArgumentException("The requested range must be a contiguous run of the command type.", nameof(count));
        }

        if (typeof(TCommand) == typeof(FillRectCommand2D))
        {
            return ((TCommand[])(object)_fillRect).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(FillRoundedRectCommand2D))
        {
            return ((TCommand[])(object)_fillRoundedRect).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(FillCircleCommand2D))
        {
            return ((TCommand[])(object)_fillCircle).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawLineCommand2D))
        {
            return ((TCommand[])(object)_drawLine).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(FillPathCommand2D))
        {
            return ((TCommand[])(object)_fillPath).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(StrokePathCommand2D))
        {
            return ((TCommand[])(object)_strokePath).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawImageCommand2D))
        {
            return ((TCommand[])(object)_drawImage).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawCoverageMaskCommand2D))
        {
            return ((TCommand[])(object)_drawCoverageMask).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawDistanceFieldCommand2D))
        {
            return ((TCommand[])(object)_drawDistanceField).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawMeshCommand2D))
        {
            return ((TCommand[])(object)_drawMesh).AsMemory(_typeOffsets[orderStart], count);
        }

        if (typeof(TCommand) == typeof(DrawPaintGraphCommand2D))
        {
            return ((TCommand[])(object)_drawPaintGraph).AsMemory(_typeOffsets[orderStart], count);
        }

        throw new ArgumentException("The requested type is not a drawing command renderer input.");
    }

    private DrawList2D Materialize()
    {
        DrawList2D? materialized = Volatile.Read(ref _materialized);
        if (materialized is not null)
        {
            return materialized;
        }

        materialized = new(_retainedPages!.Pages.SelectMany(page => page.Commands));
        return Interlocked.CompareExchange(ref _materialized, materialized, null) ?? materialized;
    }
}
