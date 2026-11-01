namespace Lumyte.Graphics.Drawing2D;

/// <summary>A reusable immutable CPU recording with typed command storage and a painter-order tape.</summary>
public sealed class DrawList2D
{
    private readonly int[] _typeOffsets;
    private readonly FillRectCommand2D[] _fillRect;
    private readonly FillRoundedRectCommand2D[] _fillRoundedRect;
    private readonly FillCircleCommand2D[] _fillCircle;
    private readonly DrawLineCommand2D[] _drawLine;
    private readonly FillPathCommand2D[] _fillPath;
    private readonly StrokePathCommand2D[] _strokePath;
    private readonly DrawImageCommand2D[] _drawImage;
    private readonly DrawCoverageMaskCommand2D[] _drawCoverageMask;
    private readonly DrawDistanceFieldCommand2D[] _drawDistanceField;
    private readonly DrawMeshCommand2D[] _drawMesh;
    private readonly DrawPaintGraphCommand2D[] _drawPaintGraph;

    internal DrawList2D(IEnumerable<DrawingCommand2D> commands)
    {
        DrawingCommand2D[] ordered = commands.ToArray();
        Commands = Array.AsReadOnly(ordered);
        _typeOffsets = new int[ordered.Length];

        // Count once so immutable typed storage never grows through temporary list arrays.
        Span<int> counts = stackalloc int[11];
        counts.Clear();
        foreach (DrawingCommand2D command in ordered)
        {
            switch (command)
            {
                case FillRectCommand2D:
                    counts[0]++;
                    break;
                case FillRoundedRectCommand2D:
                    counts[1]++;
                    break;
                case FillCircleCommand2D:
                    counts[2]++;
                    break;
                case DrawLineCommand2D:
                    counts[3]++;
                    break;
                case FillPathCommand2D:
                    counts[4]++;
                    break;
                case StrokePathCommand2D:
                    counts[5]++;
                    break;
                case DrawImageCommand2D:
                    counts[6]++;
                    break;
                case DrawCoverageMaskCommand2D:
                    counts[7]++;
                    break;
                case DrawDistanceFieldCommand2D:
                    counts[8]++;
                    break;
                case DrawMeshCommand2D:
                    counts[9]++;
                    break;
                case DrawPaintGraphCommand2D:
                    counts[10]++;
                    break;
            }
        }

        _fillRect = counts[0] == 0 ? [] : new FillRectCommand2D[counts[0]];
        _fillRoundedRect = counts[1] == 0 ? [] : new FillRoundedRectCommand2D[counts[1]];
        _fillCircle = counts[2] == 0 ? [] : new FillCircleCommand2D[counts[2]];
        _drawLine = counts[3] == 0 ? [] : new DrawLineCommand2D[counts[3]];
        _fillPath = counts[4] == 0 ? [] : new FillPathCommand2D[counts[4]];
        _strokePath = counts[5] == 0 ? [] : new StrokePathCommand2D[counts[5]];
        _drawImage = counts[6] == 0 ? [] : new DrawImageCommand2D[counts[6]];
        _drawCoverageMask = counts[7] == 0 ? [] : new DrawCoverageMaskCommand2D[counts[7]];
        _drawDistanceField = counts[8] == 0 ? [] : new DrawDistanceFieldCommand2D[counts[8]];
        _drawMesh = counts[9] == 0 ? [] : new DrawMeshCommand2D[counts[9]];
        _drawPaintGraph = counts[10] == 0 ? [] : new DrawPaintGraphCommand2D[counts[10]];
        counts.Clear();
        for (int i = 0; i < ordered.Length; i++)
        {
            switch (ordered[i])
            {
                case FillRectCommand2D command:
                    _typeOffsets[i] = counts[0];
                    _fillRect[counts[0]++] = command;
                    break;
                case FillRoundedRectCommand2D command:
                    _typeOffsets[i] = counts[1];
                    _fillRoundedRect[counts[1]++] = command;
                    break;
                case FillCircleCommand2D command:
                    _typeOffsets[i] = counts[2];
                    _fillCircle[counts[2]++] = command;
                    break;
                case DrawLineCommand2D command:
                    _typeOffsets[i] = counts[3];
                    _drawLine[counts[3]++] = command;
                    break;
                case FillPathCommand2D command:
                    _typeOffsets[i] = counts[4];
                    _fillPath[counts[4]++] = command;
                    break;
                case StrokePathCommand2D command:
                    _typeOffsets[i] = counts[5];
                    _strokePath[counts[5]++] = command;
                    break;
                case DrawImageCommand2D command:
                    _typeOffsets[i] = counts[6];
                    _drawImage[counts[6]++] = command;
                    break;
                case DrawCoverageMaskCommand2D command:
                    _typeOffsets[i] = counts[7];
                    _drawCoverageMask[counts[7]++] = command;
                    break;
                case DrawDistanceFieldCommand2D command:
                    _typeOffsets[i] = counts[8];
                    _drawDistanceField[counts[8]++] = command;
                    break;
                case DrawMeshCommand2D command:
                    _typeOffsets[i] = counts[9];
                    _drawMesh[counts[9]++] = command;
                    break;
                case DrawPaintGraphCommand2D command:
                    _typeOffsets[i] = counts[10];
                    _drawPaintGraph[counts[10]++] = command;
                    break;
            }
        }
    }

    internal IReadOnlyList<DrawingCommand2D> Commands { get; }

    internal ReadOnlyMemory<TCommand> GetCommands<TCommand>(int orderStart, int count)
        where TCommand : DrawingCommand2D
    {
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
}
