using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class GameUiWorkloads
{
    internal static IReadOnlyList<string> Cases { get; } = Array.AsReadOnly(new[] { "game-hud-static-overlay", "game-hud-values", "game-menu-animation" });

    internal static IRenderingWorkload Create(string name, int count)
    {
        if (!Cases.Contains(name))
        {
            throw new ArgumentException("Unknown game UI workload.", nameof(name));
        }

        return new GameUiWorkload(name, count);
    }

    private sealed class GameUiWorkload : IRenderingWorkload
    {
        private readonly UiWorkloadText _text = new();
        private readonly UiSurface _surface;
        private readonly Signal<string> _score = Signal.Create("Score 000000");
        private readonly Signal<float?> _health = Signal.Create<float?>(220);
        private readonly int _count;
        private DrawList2D _ui;
        private bool _dirty;
        private long _changedInputs;
        private long _recordCalls;
        private long _layoutBoundaries;

        internal GameUiWorkload(string name, int count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            Name = name;
            _count = count;
            _surface = new UiSurface(Build);
            _surface.SetSignalUpdatesEnabled(false);
            _surface.SetViewport(new(1280, 720));
            _surface.UpdateLayout();
            _ui = UiWorkloadText.Capture(_surface);
        }

        public string Suite => "game-ui";

        public string Name { get; }

        public int Frame { get; private set; }

        public string Description => Name switch
        {
            "game-hud-static-overlay" => "Moving 2D world plus real static UiSurface/Parley single-line read-only TextBoxes; benchmark adapter reuses the completed HUD recording as a separate ordered layer.",
            "game-hud-values" => "Moving world plus two actual Signals (score text and health width), reconciled by UiSurface; numeric single-line TextBox reshapes with native Parley.",
            _ => "Deterministic 60-Hz logical ticks rebuild real menu declarations with changing width/spacing; actual UiSurface reconciliation and layout, not a production animation-clock benchmark.",
        };

        public IReadOnlyDictionary<string, long> Counters => new Dictionary<string, long>
        {
            ["logical_frames"] = Frame,
            ["changed_inputs"] = _changedInputs,
            ["world_objects"] = Name == "game-menu-animation" ? 0 : _count,
            ["ui_record_calls"] = _recordCalls,
            ["layout_boundaries"] = _layoutBoundaries,
            ["text_layout_calls"] = _text.LayoutCalls,
            ["text_layout_ticks"] = _text.LayoutTicks,
            ["static_recording_cache_policy"] = Name == "game-hud-static-overlay" ? 1 : 0,
        };

        public void Update()
        {
            Frame++;
            if (Name == "game-hud-values")
            {
                _score.Value = $"Score {Frame % 1000000:D6}";
                _health.Value = 40 + (Frame % 181);
                _changedInputs += 2;
                _dirty = true;
            }
            else if (Name == "game-menu-animation")
            {
                _surface.Rebuild(Build());
                _changedInputs += 2;
                _dirty = true;
            }
        }

        public void Layout()
        {
            _surface.UpdateLayout();
            _layoutBoundaries++;
        }

        public IReadOnlyList<DrawList2D> Record()
        {
            if (_dirty)
            {
                _ui = UiWorkloadText.Capture(_surface);
                _recordCalls++;
                _dirty = false;
            }

            if (Name == "game-menu-animation")
            {
                return Array.AsReadOnly(new[] { _ui });
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.FillRect(new(0, 0, 1280, 720), new Color2D(0.035f, 0.07f, 0.12f, 1));
            for (int index = 0; index < _count; index++)
            {
                float x = ((index * 67L) + (Frame * 3L)) % 1280;
                float y = 220 + ((index * 43L) % 480);
                canvas.FillRect(new(x, y, 14, 14), new Color2D(0.2f, 0.6f, 0.75f, 1));
            }

            return Array.AsReadOnly(new[] { canvas.End(), _ui });
        }

        public void Verify()
        {
            UiWorkloadText.VerifyLayers(Record());
            if (_text.LayoutCalls == 0)
            {
                throw new InvalidOperationException("The game UI must use real native text layouts.");
            }
        }

        public void Dispose()
        {
            _surface.Dispose();
            _text.Dispose();
        }

        private Widget Build()
        {
            if (Name == "game-menu-animation")
            {
                float phase = (Frame % 120) / 60f;
                float width = 320 + (80 * (1 - MathF.Abs(phase - 1)));
                UI.Compose.Definitions.Stack menu = UiWorkloadText.Column(
                    "menu",
                    width,
                    _text.Label("PAUSED - mission control", "title", width),
                    _text.Label("Resume mission", "resume", width),
                    _text.Label("Inventory and equipment", "inventory", width),
                    _text.Label("Settings", "settings", width),
                    _text.Label("Return to base", "quit", width));
                menu.Spacing = 5 + (Frame % 20);
                return UiWorkloadText.Backed("menu-panel", menu, width, 320);
            }

            UI.TextEditing.Compose.Definitions.TextBox score = Lumyte.UI.TextEditing.Compose.TextBox(text: _score, layoutProvider: _text, isReadOnly: true, width: 260, height: 26, key: "score");
            UI.Compose.Definitions.Rectangle health = Lumyte.UI.Compose.Rectangle(fill: new Color2D(0.2f, 0.85f, 0.35f, 1), width: _health, height: 14, key: "health");
            UI.Compose.Definitions.Stack hudRow = UiWorkloadText.Row("hud-row", UiWorkloadText.Column("status", 280, _text.Label("PILOT / HEALTH", "pilot"), health, score), UiWorkloadText.Column("objectives", 420, _text.Label("Mission: secure relay station", "mission", 420), _text.Label("Ammo 24 / 120 | Wave 03", "ammo", 420)));
            return UiWorkloadText.Backed("hud", hudRow, 750, 110);
        }
    }
}
