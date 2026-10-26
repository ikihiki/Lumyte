namespace Lumyte.Graphics.Drawing2D;

/// <summary>A reusable immutable CPU recording with typed command storage and a painter-order tape.</summary>
public sealed class DrawList2D
{
    private readonly int[] _typeOffsets;
    private readonly FillRectCommand2D[] _fillRect;
    private readonly FillRoundedRectCommand2D[] _fillRoundedRect;
    private readonly FillCircleCommand2D[] _fillCircle;

    internal DrawList2D(IEnumerable<DrawingCommand2D> commands)
    {
        DrawingCommand2D[] ordered = commands.ToArray();
        Commands = Array.AsReadOnly(ordered);
        _typeOffsets = new int[ordered.Length];
        var fillRect = new List<FillRectCommand2D>();
        var fillRoundedRect = new List<FillRoundedRectCommand2D>();
        var fillCircle = new List<FillCircleCommand2D>();
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
            }
        }

        _fillRect = fillRect.ToArray();
        _fillRoundedRect = fillRoundedRect.ToArray();
        _fillCircle = fillCircle.ToArray();
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

        throw new ArgumentException("The requested type is not a drawing command renderer input.");
    }
}
