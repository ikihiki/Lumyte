using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal sealed class ComparisonCase
{
    private static readonly PaintGraph2D[] _palette = [PaintGraph2D.Solid(new(0.2f, 0.4f, 0.8f, 1)), PaintGraph2D.Solid(new(0.9f, 0.2f, 0.1f, 0.7f))];
    private static readonly Path2D _sharedPath = CreatePath();
    private readonly int _count;
    private readonly int _chunkSize;
    private readonly string _pattern;
    private readonly string _kind;
    private readonly bool[] _colors;
    private readonly bool[] _dirtyChunks;
    private readonly Canvas2D _canvas = new();
    private readonly object?[] _history = new object?[3];
    private readonly List<(int Index, DrawList2D Value)> _replacements = [];
    private readonly List<int> _dirtyIndices = [];
    private readonly object?[] _retainedHistory;
    private readonly int _dirtyCount;
    private DrawList2D? _flat;
    private RetainedListPrototype? _retained;
    private int _frame;

    internal ComparisonCase(int count, string pattern, string kind, int chunkSize, bool retained, bool flatten)
    {
        _count = count;
        _pattern = pattern;
        _kind = kind;
        _chunkSize = chunkSize;
        _colors = new bool[count];
        int chunks = (count + chunkSize - 1) / chunkSize;
        _dirtyChunks = retained ? new bool[chunks] : [];
        _retainedHistory = retained && flatten ? new object?[3] : [];
        _dirtyCount = pattern switch { "static" => 0, "single" => 1, "clustered" or "scattered" => Math.Max(1, count / 100), "all" => count, _ => throw new ArgumentException(pattern) };
        if (retained)
        {
            var fragments = new DrawList2D[_dirtyChunks.Length];
            for (int i = 0; i < fragments.Length; i++)
            {
                fragments[i] = RecordChunk(i);
            }

            _retained = RetainedListPrototype.Create(fragments);
            if (flatten)
            {
                _flat = Flatten();
            }
        }
        else
        {
            _flat = RecordAll();
        }
    }

    internal RetainedListPrototype Retained => _retained!;

    internal DrawList2D LegacyFrame()
    {
        ChangeInputs(false);
        if (_dirtyCount != 0)
        {
            _flat = RecordAll();
        }

        Keep(_flat!);
        return _flat!;
    }

    internal RetainedListPrototype RetainedFrame()
    {
        UpdateRetained();
        Keep(_retained!);
        return _retained!;
    }

    internal DrawList2D RetainedAndFlattenFrame()
    {
        UpdateRetained();
        if (_dirtyCount != 0)
        {
            _flat = Flatten();
        }

        Keep(_flat!);
        _retainedHistory[_frame % _retainedHistory.Length] = _retained;
        return _flat!;
    }

    internal DrawList2D MaterializeForCheck() => _retained is null ? _flat! : Flatten();

    private static Path2D CreatePath()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.CubicTo(new(0, 5), new(5, 5), new(5, 0));
        path.Close();
        return path.Build();
    }

    private void ChangeInputs(bool trackChunks)
    {
        if (trackChunks)
        {
            foreach (int index in _dirtyIndices)
            {
                _dirtyChunks[index] = false;
            }

            _dirtyIndices.Clear();
        }

        for (int i = 0; i < _dirtyCount; i++)
        {
            int index = _pattern switch
            {
                "scattered" => ((i * (_count / _dirtyCount)) + _frame) % _count,
                "all" => i,
                _ => (_frame + i) % _count,
            };
            _colors[index] = !_colors[index];
            if (trackChunks && !_dirtyChunks[index / _chunkSize])
            {
                _dirtyChunks[index / _chunkSize] = true;
                _dirtyIndices.Add(index / _chunkSize);
            }
        }

        if (trackChunks)
        {
            _dirtyIndices.Sort();
        }

        _frame = _frame == int.MaxValue ? 0 : _frame + 1;
    }

    private void UpdateRetained()
    {
        ChangeInputs(true);
        if (_dirtyCount == 0)
        {
            return;
        }

        _replacements.Clear();
        foreach (int index in _dirtyIndices)
        {
            _replacements.Add((index, RecordChunk(index)));
        }

        _retained = _retained!.ReplaceMany(_replacements);
    }

    private DrawList2D RecordAll()
    {
        Begin();
        for (int i = 0; i < _count; i++)
        {
            RecordCommand(i);
        }

        return _canvas.End();
    }

    private DrawList2D RecordChunk(int chunk)
    {
        Begin();
        int end = Math.Min(_count, (chunk + 1) * _chunkSize);
        for (int i = chunk * _chunkSize; i < end; i++)
        {
            RecordCommand(i);
        }

        return _canvas.End();
    }

    private void Begin()
    {
        _canvas.Begin();
        if (_kind != "rectangles")
        {
            _canvas.ClipRect(new(0, 0, 800, 800));
        }
    }

    private void RecordCommand(int index)
    {
        var rect = new Rect2D((index % 100) * 8, (index / 100) * 8, 7, 7);
        PaintGraph2D paint = _palette[_colors[index] ? 1 : 0];
        if (_kind == "mixed" && index % 3 == 1)
        {
            _canvas.FillCircle(new(rect.X, rect.Y), 3, paint);
        }
        else if (_kind == "mixed" && index % 3 == 2)
        {
            _canvas.FillPath(_sharedPath, paint);
        }
        else
        {
            _canvas.FillRect(rect, paint);
        }
    }

    private DrawList2D Flatten()
    {
        _canvas.Begin();
        _retained!.Visit(_canvas.Replay);
        return _canvas.End();
    }

    private void Keep(object root) => _history[_frame % _history.Length] = root;
}
