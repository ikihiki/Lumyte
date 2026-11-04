using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Public Canvas reuse or Canvas plus Scene.ReplaceContent, with identical source input.</summary>
public sealed class RectangleFixture : IDisposable
{
    private readonly Canvas2D _canvas = new();
    private readonly DrawingNodeId2D[] _nodes;
    private readonly int[] _changedNodes;
    private readonly int _width;
    private readonly int _height;
    private int _frame;

    /// <summary>Initializes a new instance of the <see cref="RectangleFixture"/> class.</summary>
    /// <param name="trace">The shared deterministic logical trace.</param>
    /// <param name="granularity">The number of commands per content node.</param>
    /// <param name="retained">Whether to use public Scene retention.</param>
    /// <param name="width">The physical target width.</param>
    /// <param name="height">The physical target height.</param>
    public RectangleFixture(RectangleTrace trace, int granularity, bool retained, int width = 1920, int height = 1080)
    {
        RectangleTrace.ValidateGranularity(granularity);
        Trace = trace;
        Granularity = granularity;
        Retained = retained;
        _width = width;
        _height = height;
        _changedNodes = trace.ChangedNodes(granularity);
        _nodes = retained ? new DrawingNodeId2D[RectangleTrace.Count / granularity] : [];
        if (retained)
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                _nodes[i] = Scene.Insert(Scene.Root, i, RecordPartition(i));
            }
        }
        else
        {
            Recording = RecordWhole();
        }
    }

    /// <summary>Gets the shared trace.</summary>
    public RectangleTrace Trace { get; }

    /// <summary>Gets the commands per content node.</summary>
    public int Granularity { get; }

    /// <summary>Gets a value indicating whether this is the retained public API route.</summary>
    public bool Retained { get; }

    /// <summary>Gets the owned flat scene.</summary>
    /// <returns>The deterministic result for the requested input.</returns>
    public DrawingScene2D Scene { get; } = new();

    /// <summary>Gets the immutable full Canvas recording.</summary>
    public DrawList2D? Recording { get; private set; }

    /// <summary>Gets the actual latest recording command count (not draw calls).</summary>
    public int RerecordedCommands { get; private set; }

    /// <summary>Gets actual logical content partitions touched by the last state transition.</summary>
    public int ChangedNodes { get; private set; }

    /// <summary>Gets actual logical elements changed by the last state transition.</summary>
    public int ChangedElements { get; private set; }

    /// <summary>Applies a precomputed logical frame; recording and ReplaceContent are part of this operation.</summary>
    /// <param name="frame">The logical frame number.</param>
    public void Apply(int frame)
    {
        RerecordedCommands = 0;
        ChangedNodes = 0;
        ChangedElements = 0;
        if ((_frame & 1) == (frame & 1) || Trace.ChangedCount == 0)
        {
            _frame = frame;
            return;
        }

        _frame = frame;
        ChangedNodes = _changedNodes.Length;
        ChangedElements = Trace.ChangedCount;
        if (Retained)
        {
            foreach (int node in _changedNodes)
            {
                Scene.ReplaceContent(_nodes[node], RecordPartition(node));
                RerecordedCommands += Granularity;
            }
        }
        else
        {
            Recording = RecordWhole();
            RerecordedCommands = RectangleTrace.Count;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Scene.Dispose();

    private DrawList2D RecordWhole()
    {
        _canvas.Begin();
        Trace.RecordRange(_canvas, _frame, 0, RectangleTrace.Count, _width, _height);
        return _canvas.End();
    }

    private DrawList2D RecordPartition(int node)
    {
        _canvas.Begin();
        Trace.RecordRange(_canvas, _frame, node * Granularity, Granularity, _width, _height);
        return _canvas.End();
    }
}
