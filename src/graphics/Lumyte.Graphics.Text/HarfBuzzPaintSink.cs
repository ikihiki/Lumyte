namespace Lumyte.Graphics.Text;

internal sealed class HarfBuzzPaintSink
{
    private const int MaximumOperationCount = 65_536;

    private readonly List<ColorPaintOperation> operations = [];
    private readonly Stack<HarfBuzzPaintStackEntry> stack = [];
    private Exception? failure;

    internal Exception? Failure => Volatile.Read(ref failure);

    internal void Add(ColorPaintOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operations.Count >= MaximumOperationCount)
        {
            throw new InvalidOperationException("A color glyph contains too many paint operations.");
        }
        operations.Add(operation);
    }

    internal void Push(
        HarfBuzzPaintStackKind kind,
        ColorPaintOperation operation,
        bool emit = true)
    {
        if (emit)
        {
            Add(operation);
        }
        stack.Push(new(kind, emit));
    }

    internal void Pop(HarfBuzzPaintStackKind expected, ColorPaintOperation operation)
    {
        if (!stack.TryPeek(out HarfBuzzPaintStackEntry actual) || actual.Kind != expected)
        {
            throw new InvalidOperationException("HarfBuzz returned an unbalanced color paint operation stack.");
        }

        if (actual.Emitted)
        {
            Add(operation);
        }
        stack.Pop();
    }

    internal void RecordFailure(Exception exception)
        => Interlocked.CompareExchange(ref failure, exception, null);

    internal bool TryBuild(out ColorPaintGlyph? glyph)
    {
        if (stack.Count != 0)
        {
            throw new InvalidOperationException("HarfBuzz returned an incomplete color paint operation stack.");
        }

        glyph = operations.Count == 0 ? null : new(operations.ToArray());
        return glyph is not null;
    }
}

internal enum HarfBuzzPaintStackKind
{
    Transform,
    Clip,
    Group,
}

internal readonly record struct HarfBuzzPaintStackEntry(
    HarfBuzzPaintStackKind Kind,
    bool Emitted);
