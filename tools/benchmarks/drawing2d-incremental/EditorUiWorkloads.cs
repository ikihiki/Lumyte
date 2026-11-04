using Lumyte.Graphics.Drawing2D;
using Lumyte.UI;
using Lumyte.UI.TextEditing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class EditorUiWorkloads
{
    internal static IReadOnlyList<string> Cases { get; } = Array.AsReadOnly(new[] { "editor-idle", "editor-line-edit", "editor-scroll-list", "editor-dock-resize", "editor-live-preview" });

    internal static IRenderingWorkload Create(string name, int count)
    {
        if (!Cases.Contains(name))
        {
            throw new ArgumentException("Unknown editor UI workload.", nameof(name));
        }

        return new EditorUiWorkload(name, count);
    }

    private sealed class EditorUiWorkload : IRenderingWorkload
    {
        private readonly UiWorkloadText _text = new();
        private readonly UiSurface _surface;
        private readonly int _totalRows;
        private readonly int _visibleRows;
        private DrawList2D _ui;
        private bool _dirty;
        private long _changedInputs;
        private long _recordCalls;
        private long _layoutBoundaries;

        internal EditorUiWorkload(string name, int count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            Name = name;
            _totalRows = count;
            _visibleRows = Math.Min(count, 20);
            _surface = new UiSurface(Build());
            _surface.SetViewport(new(1280, 720));
            _surface.UpdateLayout();
            _ui = UiWorkloadText.Capture(_surface);
        }

        public string Suite => "editor-ui";

        public string Name { get; }

        public int Frame { get; private set; }

        public string Description => Name switch
        {
            "editor-idle" => "Real mounted UiSurface and Parley single-line read-only TextBoxes; no changed input; explicit benchmark adapter reuses a completed immutable recording (not a production UI-host cache claim).",
            "editor-line-edit" => "Controlled single-line TextBox text replacement reconciles the retained TextBoxElement/TextDocument and invokes native Parley layout. Not a multiline editor, keyboard/IME, or undo benchmark.",
            "editor-scroll-list" => "Explicit benchmark-owned visible-window virtualization: only min(count,20) actual read-only single-line TextBox rows are mounted out of count logical records; keyed rows advance by one with nested production clips. Not a production virtualizing control.",
            "editor-dock-resize" => "Deterministic panel width and surface viewport changes drive actual UiSurface layout and width-dependent Parley TextBox layout. Models dock-like panel resizing, not a docking engine.",
            _ => "Static real editor panels retain their immutable UI recording; a separate clipped moving 2D preview layer is recorded above the panels. Explicit benchmark adapter cache, no replay/flatten.",
        };

        public IReadOnlyDictionary<string, long> Counters => new Dictionary<string, long>
        {
            ["logical_frames"] = Frame,
            ["changed_inputs"] = _changedInputs,
            ["visible_rows"] = _visibleRows,
            ["total_rows"] = _totalRows,
            ["first_visible_row"] = FirstRow,
            ["benchmark_virtualization_policy"] = Name == "editor-scroll-list" ? 1 : 0,
            ["static_recording_cache_policy"] = Name is "editor-idle" or "editor-live-preview" ? 1 : 0,
            ["ui_record_calls"] = _recordCalls,
            ["layout_boundaries"] = _layoutBoundaries,
            ["text_layout_calls"] = _text.LayoutCalls,
            ["text_layout_ticks"] = _text.LayoutTicks,
        };

        private int FirstRow => Name == "editor-scroll-list" ? Frame % (_totalRows - _visibleRows + 1) : 0;

        private string EditText => Name == "editor-line-edit" ? $"entity.position.x = {Frame % 10000:D4}; // controlled edit" : "entity.position.x = 0042;";

        public void Update()
        {
            Frame++;
            if (Name is "editor-line-edit" or "editor-scroll-list" or "editor-dock-resize")
            {
                _surface.Rebuild(Build());
                _changedInputs++;
                _dirty = true;
                if (Name == "editor-dock-resize")
                {
                    _surface.SetViewport(new(1200 + (Frame % 81), 720));
                    _changedInputs++;
                }
            }
            else if (Name == "editor-live-preview")
            {
                _changedInputs++;
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

            if (Name != "editor-live-preview")
            {
                return Array.AsReadOnly(new[] { _ui });
            }

            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.Save();
            canvas.ClipRect(new(350, 60, 570, 500));
            canvas.FillRect(new(350, 60, 570, 500), new Color2D(0.04f, 0.05f, 0.08f, 1));
            for (int index = 0; index < _totalRows; index++)
            {
                float x = 350 + (((index * 31L) + (Frame * 2L)) % 570);
                float y = 60 + ((index * 47L) % 500);
                canvas.FillRect(new(x, y, 12, 12), new Color2D(0.25f, 0.7f, 0.9f, 1));
            }

            canvas.Restore();
            return Array.AsReadOnly(new[] { _ui, canvas.End() });
        }

        public void Verify()
        {
            UiWorkloadText.VerifyLayers(Record());
            List<TextBoxElement> editors = [];
            CollectEditors(_surface.RootElement, editors);
            if (_text.LayoutCalls == 0 || editors.Any(editor => editor.CurrentLayout is null))
            {
                throw new InvalidOperationException("Every mounted TextBox requires a real Parley layout.");
            }

            TextBoxElement line = editors.Single(editor => editor.Widget.Key == "edit-line");
            string expected = EditText;
            if (line.Document.CaptureSnapshot().CommittedText != expected)
            {
                throw new InvalidOperationException("Controlled text did not reach the real mounted TextDocument.");
            }
        }

        public void Dispose()
        {
            _surface.Dispose();
            _text.Dispose();
        }

        private static void CollectEditors(UiElement element, List<TextBoxElement> editors)
        {
            if (element is TextBoxElement editor)
            {
                editors.Add(editor);
            }

            foreach (UiElement child in element.Children)
            {
                CollectEditors(child, editors);
            }
        }

        private Widget Build()
        {
            float leftWidth = Name == "editor-dock-resize" ? 250 + (Frame % 101) : 320;
            var rows = new Widget[_visibleRows + 1];
            rows[0] = _text.Label("SCENE / Assets", "scene-title", leftWidth);
            for (int index = 0; index < _visibleRows; index++)
            {
                int id = FirstRow + index;
                rows[index + 1] = _text.Label($"Node {id:D5} / Mesh instance", $"node-{id}", leftWidth);
            }

            UI.Compose.Definitions.Panel list = UiWorkloadText.Backed("list-clip", UiWorkloadText.Column("rows", leftWidth, rows), leftWidth, 640);
            UI.Compose.Definitions.Stack inspector = UiWorkloadText.Column(
                "inspector",
                300,
                _text.Label("INSPECTOR", "inspector-title"),
                _text.Label("Transform / enabled", "component"),
                _text.Label("Position X: 42.0", "position"),
                _text.Label("Material: brushed steel", "material"),
                _text.Label("Render layer: Default", "layer"));
            UI.Compose.Definitions.Stack center = UiWorkloadText.Column(
                "center",
                580,
                _text.Label("Scene preview / perspective", "preview-title", 570),
                _text.Label(EditText, "edit-line", 570, editable: true),
                _text.Label("Status: scene saved | 60 Hz", "status", 570));
            return UiWorkloadText.Backed("workspace", UiWorkloadText.Row("docked-panels", list, center, inspector), 1280, 720);
        }
    }
}
