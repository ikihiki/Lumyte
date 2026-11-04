using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI;
using Stack = Lumyte.UI.Compose.Definitions.Stack;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal sealed class PublicRetainedApiProbeInputs : IDisposable
{
    private readonly DrawingScene2D? _scene;
    private readonly DrawingNodeId2D[] _nodes = [];
    private readonly Signal<Color2D>? _fill;
    private readonly UiSurface? _surface;
    private readonly UiDrawingScene2D? _drawing;
    private readonly int _partitions;
    private int _state;

    internal PublicRetainedApiProbeInputs(int partitions)
    {
        _partitions = partitions;
        RectangleCount = 1024;
        _scene = new();
        _nodes = new DrawingNodeId2D[partitions];
        for (int node = 0; node < partitions; node++)
        {
            _nodes[node] = _scene.Insert(_scene.Root, node, RecordPartition(node));
        }
    }

    internal PublicRetainedApiProbeInputs(int count, bool ui, bool flatCpuFixture = false)
    {
        if (!ui)
        {
            throw new ArgumentException("Use the partition constructor for non-UI inputs.");
        }

        RectangleCount = count;
        LayoutKind = flatCpuFixture ? "flat-panel-overlapping-siblings" : "grid-stacks";
        int columns = flatCpuFixture ? count : (int)Math.Ceiling(Math.Sqrt(count));
        float size = flatCpuFixture ? 256 : 256f / columns;
        _fill = Signal.Create(Color(0));
        var rows = new List<Widget>();
        var flatChildren = new List<Widget>();
        for (int first = 0; first < count; first += columns)
        {
            var children = new List<Widget>();
            for (int index = first; index < Math.Min(first + columns, count); index++)
            {
                // Only the last visible leaf is reactive; no custom element or
                // benchmark layer adapter participates in the production adapter.
                children.Add(index == count - 1
                    ? Compose.Rectangle(fill: _fill, width: size, height: size)
                    : new Compose.Definitions.Rectangle { Fill = Color(0), Width = size, Height = size });
            }

            if (flatCpuFixture)
            {
                flatChildren.AddRange(children);
                continue;
            }

            var row = new Stack { Orientation = UiOrientation.Horizontal, Width = 256, Height = size };
            row.Children.Initialize(children.ToArray());
            rows.Add(row);
        }

        Compose.Definitions.Panel root;
        if (flatCpuFixture)
        {
            // Preserve N siblings under one parent: grid containers would hide
            // the scene Move sibling-search scaling this CPU profile tests.
            root = new Compose.Definitions.Panel { Width = 256, Height = 256 };
            root.Children.Initialize(flatChildren.ToArray());
            UiElementCount = 1 + count;
        }
        else
        {
            root = new Stack { Width = 256, Height = 256 };
            root.Children.Initialize(rows.ToArray());
            UiElementCount = 1 + rows.Count + count;
        }

        _surface = new(root);
        _surface.SetViewport(new(256, 256));
        _drawing = _surface.CreateDrawingScene();
        _drawing.Update();
    }

    internal string LayoutKind { get; } = "constant-command-partitions";

    internal int RectangleCount { get; }

    internal int? UiElementCount { get; }

    internal int? SceneNodeCount => _scene is null ? null : _nodes.Length + 1;

    internal int? ContentNodeCount => _scene is null ? null : _nodes.Length;

    internal int? LastRecordingCount => _drawing?.LastRecordingCount;

    internal int ExpectedChangedRecordings => LayoutKind == "flat-panel-overlapping-siblings" ? 2 : 3;

    internal DrawingScene2D Scene => _drawing?.Scene ?? _scene!;

    internal bool IsUi => _surface is not null;

    public void Dispose()
    {
        _drawing?.Dispose();
        _surface?.Dispose();
        _scene?.Dispose();
    }

    internal void ChangeInput()
    {
        _state ^= 1;
        if (_fill is not null)
        {
            _fill.Value = Color(_state);
        }
        else
        {
            // New public Canvas recording of the entire affected node: exactly
            // one command changes; all other commands retain their value/order.
            _scene!.ReplaceContent(_nodes[^1], RecordPartition(_partitions - 1));
        }
    }

    internal void Update() => _drawing?.Update();

    internal DrawList2D Reference()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (_surface is not null)
        {
            _surface.Record(canvas);
        }
        else
        {
            // Independent direct Canvas stream, not replay of scene recordings.
            for (int index = 0; index < RectangleCount; index++)
            {
                Rect(canvas, index);
            }
        }

        return canvas.End();
    }

    private static Color2D Color(int state) => state == 0 ? new(0.2f, 0.4f, 0.8f, 1) : new(0.8f, 0.2f, 0.1f, 1);

    private DrawList2D RecordPartition(int node)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        int perNode = RectangleCount / _partitions;
        for (int index = node * perNode; index < (node + 1) * perNode; index++)
        {
            Rect(canvas, index);
        }

        DrawList2D recording = canvas.End();
        if (recording.Count != perNode)
        {
            throw new InvalidOperationException("Public command count mismatch.");
        }

        return recording;
    }

    private void Rect(Canvas2D canvas, int index) => canvas.FillRect(
        new((index % 32) * 8, (index / 32) * 8, 7, 7),
        Color(index == RectangleCount - 1 ? _state : 0));
}
