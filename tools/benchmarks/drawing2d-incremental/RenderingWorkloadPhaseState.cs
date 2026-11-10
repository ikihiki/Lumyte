using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Benchmark-only stage orchestration. All counter reads, semantic checks, and snapshot oracles are untimed.
internal sealed class RenderingWorkloadPhaseState(IRenderingWorkload workload) : IDisposable
{
    private readonly IReadOnlyList<DrawList2D>?[] _history = new IReadOnlyList<DrawList2D>?[3];
    private IReadOnlyDictionary<string, long> _before = null!;
    private IReadOnlyList<DrawList2D> _initial = null!;
    private DrawingCommand2D[][] _initialCommands = null!;
    private PathSegment2D[][][] _initialPaths = null!;
    private long _textLayoutsBeforeFrame;
    private int _beforeFrame;
    private int _slot;
    private bool _disposed;

    internal static IEnumerable<string> SelectedCases => RenderingWorkloadCatalog.Select(
        Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_SUITE") ?? "all",
        Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_CASE"));

    internal static IEnumerable<int> SelectedCounts => [int.Parse(
        Environment.GetEnvironmentVariable("LUMYTE_WORKLOAD_COUNT") ?? "200",
        System.Globalization.CultureInfo.InvariantCulture)];

    internal IRenderingWorkload Workload { get; } = workload;

    /// <summary>Checks retained semantics and releases the fixture even when verification throws.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            CheckInitialSnapshot();
            Workload.Verify();
        }
        finally
        {
            Array.Clear(_history);
            _initial = null!;
            _initialCommands = null!;
            _initialPaths = null!;
            Workload.Dispose();
        }
    }

    internal static RenderingWorkloadPhaseState Create(string name, int count)
    {
        var state = new RenderingWorkloadPhaseState(RenderingWorkloadCatalog.Create(name, count));
        try
        {
            state.Workload.Layout();
            state._initial = state.Record();
            state._initialCommands = state._initial.Select(layer => layer.Commands.ToArray()).ToArray();
            state._initialPaths = state._initialCommands.Select(commands => commands.Select(command =>
                command is FillPathCommand2D path ? path.Path.Segments.ToArray() : Array.Empty<PathSegment2D>()).ToArray()).ToArray();
            state.Workload.Verify();

            // Exercise each operation twice before BDN timing. This detects a missing deterministic
            // input advance that would turn the second dynamic Record into a cached return.
            for (int repeat = 0; repeat < 2; repeat++)
            {
                state.Prepare("Update");
                state.Workload.Update();
                state.Check("Update");
                if (state.Workload.Suite != "2d")
                {
                    state.Prepare("Layout");
                    state.Workload.Layout();
                    state.Check("Layout");
                }

                state.Prepare("Record");
                state.Record();
                state.Check("Record");
            }

            return state;
        }
        catch
        {
            // Construction/verification failure must not leak the native text engine.
            state.Workload.Dispose();
            throw;
        }
    }

    internal void Prepare(string operation)
    {
        _textLayoutsBeforeFrame = Workload.Counters.GetValueOrDefault("text_layout_calls");
        if (operation is "Layout" or "Record")
        {
            Workload.Update();
        }

        if (operation == "Record" && Workload.Suite != "2d")
        {
            Workload.Layout();
        }

        _beforeFrame = Workload.Frame;

        // Some fixture counter getters allocate; never put them inside measured operations.
        _before = Workload.Counters.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    internal IReadOnlyList<DrawList2D> Record()
    {
        IReadOnlyList<DrawList2D> layers = Workload.Record();
        _history[_slot] = layers;
        _slot = (_slot + 1) % _history.Length;
        return layers;
    }

    internal void Check(string operation)
    {
        IReadOnlyDictionary<string, long> after = Workload.Counters;
        int expectedFrame = checked(_beforeFrame + (operation == "Update" ? 1 : 0));
        Require(Workload.Frame == expectedFrame, "Only Update may advance exactly one deterministic frame.");
        if (Workload.Suite == "2d")
        {
            Require(Delta(after, "updateCalls") == (operation == "Update" ? 1 : 0), "Unexpected 2D Update call count.");
            Require(Delta(after, "layoutNoOpCalls") == 0, "An empty 2D Layout must not be part of an isolated operation.");
            Require(Delta(after, "recordCalls") == (operation == "Record" ? 1 : 0), "Unexpected 2D Record call count.");
            if (operation == "Record")
            {
                bool cached = Workload.Name == "2d-static";
                Require(Delta(after, "canvasRecordings") == (cached ? 0 : 1), "Dynamic recording unexpectedly hit a cache.");
                Require(Delta(after, "cachedRecordReturns") == (cached ? 1 : 0), "Unexpected recording-cache classification.");
            }
        }
        else
        {
            Require(Delta(after, "layout_boundaries") == (operation == "Layout" ? 1 : 0), "Unexpected UI Layout boundary count.");
            bool cached = after["static_recording_cache_policy"] == 1;
            Require(Delta(after, "ui_record_calls") == (operation == "Record" && !cached ? 1 : 0), "Unexpected UI recording/cache policy.");
            if (operation == "Update")
            {
                bool changesInputs = Workload.Name is not ("game-hud-static-overlay" or "editor-idle");
                Require((Delta(after, "changed_inputs") > 0) == changesInputs, "Unexpected deterministic input mutation policy.");
            }
        }

        // Finish the frame only after validating the timed operation's exact boundary.
        if (operation == "Update" && Workload.Suite != "2d")
        {
            Workload.Layout();
        }

        if (operation != "Record")
        {
            Record();
        }

        foreach (IReadOnlyList<DrawList2D>? layers in _history)
        {
            if (layers is not null)
            {
                UiWorkloadText.VerifyLayers(layers);
            }
        }

        if (Workload.Suite != "2d")
        {
            long textLayouts = Workload.Counters["text_layout_calls"] - _textLayoutsBeforeFrame;
            if (Workload.Name is "game-hud-static-overlay" or "editor-idle" or "editor-live-preview")
            {
                Require(textLayouts == 0, "A cached static UI stage unexpectedly reshaped text.");
            }
            else if (Workload.Name is "game-hud-values" or "editor-line-edit" or "editor-dock-resize")
            {
                // Text can be nested in Update/reconciliation OR Layout. Check the completed
                // logical operation cycle, without pretending that text is an additive stage.
                Require(textLayouts > 0, "Changing text/width did not exercise real native text layout.");
            }
        }

        CheckInitialSnapshot();

        // Fixture Verify may call Record itself: it is deliberately after the operation-counter checks.
        Workload.Verify();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private long Delta(IReadOnlyDictionary<string, long> after, string key) => after[key] - _before[key];

    private void CheckInitialSnapshot()
    {
        for (int layer = 0; layer < _initial.Count; layer++)
        {
            Require(_initial[layer].Commands.Count == _initialCommands[layer].Length, "Retained CPU snapshot count changed.");
            for (int command = 0; command < _initialCommands[layer].Length; command++)
            {
                DrawingCommand2D current = _initial[layer].Commands[command];
                Require(ReferenceEquals(current, _initialCommands[layer][command]), "Retained CPU command changed.");
                if (current is FillPathCommand2D path)
                {
                    Require(path.Path.Segments.SequenceEqual(_initialPaths[layer][command]), "Retained CPU path geometry changed.");
                }
            }
        }
    }
}
