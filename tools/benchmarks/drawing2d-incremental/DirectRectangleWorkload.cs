using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Uses the exact ComparisonCase geometry, palette and mutation sequence, without Canvas/End.
internal sealed class DirectRectangleWorkload
{
    private static readonly PaintGraph2D[] _palette = [PaintGraph2D.Solid(new(0.2f, 0.4f, 0.8f, 1)), PaintGraph2D.Solid(new(0.9f, 0.2f, 0.1f, 0.7f))];
    private static readonly DrawingState2D _state = new(Matrix3x2.Identity, ClipStack2D.Empty);
    private readonly bool[] _colors;
    private readonly string _pattern;
    private readonly int _dirtyCount;
    private readonly List<(int Index, FillRectCommand2D Value)> _changes = [];
    private readonly DirectRectangleScene?[] _history = new DirectRectangleScene?[3];
    private int _frame;

    internal DirectRectangleWorkload(int count, string pattern)
    {
        _colors = new bool[count];
        _pattern = pattern;
        _dirtyCount = pattern switch { "static" => 0, "single" => 1, "clustered" or "scattered" => Math.Max(1, count / 100), "all" => count, _ => throw new ArgumentException(pattern) };
        Scene = DirectRectangleScene.Create(Enumerable.Range(0, count).Select(Command).ToArray());
    }

    internal DirectRectangleScene Scene { get; private set; }

    internal int PackedCommands { get; private set; }

    internal long CopiedCpuBytes { get; private set; }

    internal DirectRectangleScene Update()
    {
        _changes.Clear();
        for (int i = 0; i < _dirtyCount; i++)
        {
            int index = _pattern switch
            {
                "scattered" => ((i * (_colors.Length / _dirtyCount)) + _frame) % _colors.Length,
                "all" => i,
                _ => (_frame + i) % _colors.Length,
            };
            _colors[index] = !_colors[index];
            _changes.Add((index, Command(index)));
        }

        // Wrapping clustered and scattered requests must obey the scene's sorted-unique contract.
        _changes.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        Scene = Scene.Update(_changes);
        PackedCommands = _changes.Count == 0 ? 0 : Scene.LastPackedCommands;
        CopiedCpuBytes = _changes.Count == 0 ? 0 : Scene.CopiedCpuBytes;
        _frame = _frame == int.MaxValue ? 0 : _frame + 1;
        _history[_frame % _history.Length] = Scene;
        return Scene;
    }

    private FillRectCommand2D Command(int index) => new(new((index % 100) * 8, (index / 100) * 8, 7, 7), _palette[_colors[index] ? 1 : 0], _state);
}
