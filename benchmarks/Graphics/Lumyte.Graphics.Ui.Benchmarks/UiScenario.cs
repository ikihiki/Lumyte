using Lumyte.Graphics.Drawing2D;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.TextEditing;
using Stack = Lumyte.UI.Compose.Definitions.Stack;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.UiScenarios;

internal sealed class UiScenario : IDisposable
{
    private readonly UiWorkloadText _text;
    private readonly Signal<string> _score;
    private readonly Signal<float?> _health;
    private readonly Canvas2D _canvas = new();
    private UiSurface _surface;
    private DrawList2D? _cachedUi;
    private bool _dirty = true;

    internal UiScenario(UiProfile profile, int initialFrame = 0)
    {
        _text = new(profile.Id == "U04" && profile.VisibleRows == 100 ? 8 : 17);
        Profile = profile;
        Input = UiInput.At(profile, initialFrame);
        _score = Signal.Create(Score(Input));
        _health = Signal.Create<float?>(Health(Input));
        _surface = new UiSurface(Build());
        _surface.SetSignalUpdatesEnabled(false);
        SetViewport();
        _surface.UpdateLayout();
    }

    internal UiProfile Profile { get; }

    internal UiInput Input { get; private set; }

    internal long SignalSets { get; private set; }

    internal long Rebuilds { get; private set; }

    internal long LayoutCalls { get; private set; }

    internal long UiRecordings { get; private set; }

    internal long CachedReturns { get; private set; }

    internal long TextLayoutCalls => _text.LayoutCalls;

    internal long TextLayoutTicks => _text.LayoutTicks;

    internal string FontHash => _text.FontHash;

    /// <inheritdoc/>
    public void Dispose()
    {
        _surface.Dispose();
        _text.Dispose();
    }

    internal void Update(int frame)
    {
        var next = UiInput.At(Profile, frame);
        int previous = Input.UiRevision;
        Input = next;
        if (Profile.Id == "U01" && previous != next.UiRevision)
        {
            // Real generated Signal bindings; reconciliation is performed by UpdateLayout.
            _score.Value = Score(next);
            _health.Value = Health(next);
            SignalSets += 2;
            _dirty = true;
        }
        else if (previous != next.UiRevision)
        {
            _surface.Rebuild(Build());
            Rebuilds++;
            _dirty = true;
        }

        if (Profile.Id == "U06")
        {
            SetViewport();
        }
    }

    internal void Layout()
    {
        _surface.UpdateLayout();
        LayoutCalls++;
    }

    internal IReadOnlyList<DrawList2D> Record(bool forceFresh = false)
    {
        // Moving menu/scroll uses direct public Canvas transforms around actual surface recording,
        // not replay of an old recording. Static layout work is kept distinct from recording work.
        bool placement = Profile.Id == "U04" || (Profile.Id == "U02" && Profile.Variant == "transform");
        if (_dirty || forceFresh || placement || _cachedUi is null)
        {
            _canvas.Begin();
            _canvas.Scale(new(Input.Dpr));
            if (Profile.Id == "U04")
            {
                _canvas.ClipRect(new(0, 0, 700, 1040));
                _canvas.Translate(new(0, -Input.Scroll));
            }

            if (Profile.Id == "U02" && Profile.Variant == "transform")
            {
                _canvas.Translate(new(20 + (Input.Frame % 60), 40));
            }

            _surface.Record(_canvas);
            _cachedUi = _canvas.End();
            UiRecordings++;
            _dirty = false;
        }
        else
        {
            CachedReturns++;
        }

        // A fixed analytic sentinel outside every UI/preview region independently checks color/order.
        _canvas.Begin();
        _canvas.FillRect(new(0, 0, Input.TargetWidth, Input.TargetHeight), new Color2D(0, 0, 0, 1));
        _canvas.FillRect(new(Input.TargetWidth - 16, Input.TargetHeight - 16, 8, 8), new Color2D(1, 0, 0, 1));
        _canvas.Save();
        _canvas.ClipRect(new(Input.TargetWidth - 14, Input.TargetHeight - 14, 2, 2));
        _canvas.FillRect(new(Input.TargetWidth - 16, Input.TargetHeight - 16, 8, 8), new Color2D(0, 0, 1, 1));
        _canvas.Restore();
        if (Profile.Id == "U01")
        {
            for (int i = 0; i < 128; i++)
            {
                _canvas.FillRect(new(((i * 67) + ((Input.Frame * 180) / Profile.Hz)) % 1800, 300 + ((i * 43) % 650), 14, 14), new Color2D(0.2f, 0.6f, 0.75f, 1));
            }
        }

        DrawList2D background = _canvas.End();
        if (Profile.Id != "U07")
        {
            return [background,
            _cachedUi!];
        }

        _canvas.Begin();
        _canvas.ClipRect(new(800, 80, 900, 800));
        for (int i = 0; i < 128; i++)
        {
            _canvas.FillRect(new(800 + (((i * 31) + (Input.Frame * 2)) % 900), 80 + ((i * 47) % 800), 12, 12), new Color2D(0.25f, 0.7f, 0.9f, 1));
        }

        return [background,
        _cachedUi!,
        _canvas.End()];
    }

    // Independent mount from pure logical input: does not access live UI recordings or flatten Scene.
    // This tests incremental reconciliation/cache correctness, not independence of the UI layout algorithm.
    internal IReadOnlyList<DrawList2D> Reference()
    {
        using var independent = new UiScenario(Profile, Input.Frame);
        independent.Layout();
        return independent.Record(forceFresh: true);
    }

    internal object Verify()
    {
        // UiSurface roots are tight to the viewport; the painted background must retain
        // its own explicit logical panel size rather than covering the HUD world/probes.
        if (Profile.Id != "U04")
        {
            UiElement background = _surface.RootElement.Children[0];
            float expectedWidth = Profile.Id == "U01" ? 600 : Input.PanelWidth;
            float expectedHeight = Profile.Id switch
            {
                "U01" => 120,
                "U02" => 320,
                "U05" => 300,
                _ => 700,
            };
            if (background.Size.X != expectedWidth || background.Size.Y != expectedHeight)
            {
                throw new InvalidOperationException("Painted UI background escaped its declared panel extent.");
            }
        }

        var editors = new List<TextBoxElement>();
        Collect(_surface.RootElement, editors);
        if (editors.Count == 0 || editors.Any(e => e.CurrentLayout is null || e.CurrentLayout.Drawing.Drawings.Count == 0))
        {
            throw new InvalidOperationException("Missing real mounted native text layout.");
        }

        foreach (TextBoxElement e in editors)
        {
            string actual = e.Document.CaptureSnapshot().CommittedText;
            if (actual != ((Lumyte.UI.TextEditing.Compose.Definitions.TextBox)e.Widget).Text)
            {
                throw new InvalidOperationException("Controlled text was not published to real TextDocument.");
            }

            _text.CheckGlyphs(actual, Math.Max(1, e.Size.X));
        }

        if (Profile.Id == "U04" && editors.Count != Profile.VisibleRows)
        {
            throw new InvalidOperationException("Visible window did not mount the requested rows.");
        }

        if (Profile.Id == "U05" && Profile.Variant == "wrap" && editors[0].CurrentLayout!.Drawing.LineCount < 2)
        {
            throw new InvalidOperationException("Wrap profile did not actually wrap.");
        }

        return new
        {
            check = "ui-native-state",
            Profile.Key,
            Input.Frame,
            Input.InputId,
            mountedText = editors.Count,
            glyphs = _text.CheckGlyphs(editors[0].Document.CaptureSnapshot().CommittedText, Math.Max(1, editors[0].Size.X)),
            status = "passed",
        };
    }

    internal object Counters() => new
    {
        SignalSets,
        Rebuilds,
        LayoutCalls,
        UiRecordings,
        CachedReturns,
        TextLayoutCalls,
        TextLayoutTicks,
        logicalRows = Profile.Id == "U04" ? 10000 : 0,
        visibleRows = Profile.Id == "U04" ? Profile.VisibleRows : 0,
        Input.FirstRow,
        Input.Scroll,
        Input.UiRevision,
        Input.Dpr,
        Input.TargetWidth,
        Input.TargetHeight,
        benchmarkOwnedVirtualization = Profile.Id == "U04",
        recordingCache = "benchmark-owned completed DrawList; no Replay",
        preview = "2d-only",
    };

    private static string Score(UiInput s) => $"Score {s.HudValue % 1000000:D6}";

    private static float Health(UiInput s) => 40 + (s.HudValue % 181);

    private static void Collect(UiElement e, List<TextBoxElement> result)
    {
        if (e is TextBoxElement t)
        {
            result.Add(t);
        }

        foreach (UiElement c in e.Children)
        {
            Collect(c, result);
        }
    }

    private void SetViewport() => _surface.SetViewport(new((float)Input.TargetWidth / Input.Dpr, (float)Input.TargetHeight / Input.Dpr));

    private Widget Build()
    {
        if (Profile.Id == "U01")
        {
            Lumyte.UI.TextEditing.Compose.Definitions.TextBox score = Lumyte.UI.TextEditing.Compose.TextBox(text: _score, layoutProvider: _text, isReadOnly: true, width: 300, height: 28, key: "score");
            Lumyte.UI.Compose.Definitions.Rectangle health = Lumyte.UI.Compose.Rectangle(fill: new Color2D(0.2f, 0.85f, 0.35f, 1), width: _health, height: 14, key: "health");
            return UiWorkloadText.Backed("hud", UiWorkloadText.Column("hud-column", 600, _text.Label("PILOT / HEALTH", "pilot"), health, score), 600, 120);
        }

        if (Profile.Id == "U04")
        {
            float rowHeight = Profile.VisibleRows == 100 ? 10 : 26;
            var rows = new Widget[Profile.VisibleRows];
            for (int i = 0; i < rows.Length; i++)
            {
                int id = Input.FirstRow + i;
                Lumyte.UI.TextEditing.Compose.Definitions.TextBox label = _text.Label($"Node {id:D5} / Mesh instance", $"node-{id}", 680);
                label.Height = rowHeight;
                rows[i] = label;
            }

            Stack list = UiWorkloadText.Column("rows", 700, rows);
            list.Spacing = 0;
            return list;
        }

        if (Profile.Id == "U05")
        {
            Lumyte.UI.TextEditing.Compose.Definitions.TextBox edit = _text.Label(Input.Text, "edit-line", Input.PanelWidth, editable: true);
            edit.Height = Profile.Variant == "wrap" ? 260 : 40;
            return UiWorkloadText.Backed("edit-panel", edit, Input.PanelWidth, 300);
        }

        if (Profile.Id == "U02")
        {
            Stack menu = UiWorkloadText.Column("menu", Input.PanelWidth, _text.Label("PAUSED - mission control", "title", Input.PanelWidth), _text.Label("Resume mission", "resume", Input.PanelWidth), _text.Label("Inventory and equipment", "inventory", Input.PanelWidth), _text.Label("Settings", "settings", Input.PanelWidth));
            menu.Spacing = Profile.Variant == "layout" ? 5 + (Input.Frame % 20) : 5;
            return UiWorkloadText.Backed("menu-panel", menu, Input.PanelWidth, 320);
        }

        Stack panel = UiWorkloadText.Column("editor-panel", Input.PanelWidth, _text.Label("SCENE / Assets", "title", Input.PanelWidth), _text.Label($"Status revision {Input.UiRevision:D4}", "status", Input.PanelWidth), _text.Label("Transform / enabled / position", "position", Input.PanelWidth), _text.Label("This panel has real native text layout and a stable mounted editor surface.", "details", Input.PanelWidth));
        return UiWorkloadText.Backed("workspace", panel, Input.PanelWidth, 700);
    }
}
