using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// The independent reference is a Canvas replay of the same source recordings, not
// a copy of the production flattening/geometry algorithm or the old prototype.
internal sealed class ProductionRetainedInputs : IDisposable
{
    private readonly IRenderingWorkload? _workload;
    private readonly string _pattern;
    private readonly int _count;
    private readonly List<Placement> _placements = [];
    private readonly DrawingNodeId2D _group;
    private DrawList2D? _reference;
    private int _frame;

    internal ProductionRetainedInputs(string name, int count)
    {
        Name = name;
        _count = name.StartsWith("rect-", StringComparison.Ordinal) ? count / 2 : count;
        _pattern = name;
        _group = Scene.AddGroup(Scene.Root, 0);
        if (!name.StartsWith("rect-", StringComparison.Ordinal))
        {
            _workload = RenderingWorkloadCatalog.Create(name, count);
            _workload.Layout();
            SyncLayers(_workload.Record());
        }
        else
        {
            // Two shared-content placements make order changes visible with alpha.
            DrawList2D[] pages = Enumerable.Range(0, (_count + 63) / 64)
                .Select(p => RectanglePage(p, 0)).ToArray();
            var canvas = new Canvas2D();
            canvas.Begin();
            foreach (DrawList2D page in pages)
            {
                canvas.Replay(page);
            }

            var content = DrawingContent2D.FromDrawList(canvas.End());
            for (int i = 0; i < 2; i++)
            {
                DrawingNodeId2D id = Scene.Insert(_group, i, content);
                var placement = new Placement(id, content, (DrawList2D[])pages.Clone());
                placement.Transform = Matrix3x2.CreateTranslation(i * 1.25f, i * 0.75f);
                Scene.SetTransform(id, placement.Transform);
                _placements.Add(placement);
            }
        }
    }

    internal DrawingScene2D Scene { get; } = new();

    internal string Name { get; }

    private Matrix3x2 GroupTransform { get; set; } = Matrix3x2.Identity;

    public void Dispose()
    {
        _workload?.Dispose();
        Scene.Dispose();
    }

    internal void Advance()
    {
        _frame++;
        if (_workload is not null)
        {
            _workload.Update();
            _workload.Layout();
            SyncLayers(_workload.Record());
            return;
        }

        switch (_pattern)
        {
            case "rect-static": return;
            case "rect-page":
                // Input generation only records the changed canonical page in BOTH modes.
                ReplacePage(_placements[0], (_frame - 1) % _placements[0].Pages.Length);
                break;
            case "rect-whole":
                foreach (Placement placement in _placements)
                {
                    for (int page = 0; page < placement.Pages.Length; page++)
                    {
                        ReplacePage(placement, page);
                    }
                }

                break;
            case "rect-group-move":
                GroupTransform = Matrix3x2.CreateTranslation((_frame % 9) * 0.25f, (_frame % 7) * 0.5f);
                Scene.SetTransform(_group, GroupTransform);
                break;
            case "rect-order":
                Placement first = _placements[0];
                _placements.RemoveAt(0); _placements.Add(first);
                Scene.Move(first.Id, _group, _placements.Count - 1);
                break;
            default: throw new ArgumentException(_pattern);
        }

        _reference = null;
    }

    internal DrawList2D Reference()
    {
        if (_reference is not null)
        {
            return _reference;
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(GroupTransform);
        foreach (Placement placement in _placements)
        {
            canvas.Save();
            canvas.ConcatTransform(placement.Transform);
            foreach (DrawList2D page in placement.Pages)
            {
                canvas.Replay(page);
            }

            canvas.Restore();
        }

        return _reference = canvas.End();
    }

    internal void Verify() => _workload?.Verify();

    private void ReplacePage(Placement placement, int page)
    {
        placement.Pages[page] = RectanglePage(page, _frame);
        placement.Content = placement.Content.WithPage(page, placement.Pages[page]);
        Scene.ReplaceContent(placement.Id, placement.Content);
    }

    private DrawList2D RectanglePage(int page, int frame)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        int end = Math.Min(_count, (page * 64) + 64);
        for (int index = page * 64; index < end; index++)
        {
            float x = (index % 100) * 2.25f, y = ((index / 100) % 100) * 2.25f;
            canvas.FillRect(
                new(x + 0.125f, y + 0.375f, 3.5f, 3.5f),
                (index + frame) % 2 == 0 ? new Color2D(0.2f, 0.4f, 0.8f, 0.6f) : new Color2D(0.8f, 0.2f, 0.1f, 0.6f));
        }

        return canvas.End();
    }

    private void SyncLayers(IReadOnlyList<DrawList2D> layers)
    {
        if (_placements.Count != layers.Count)
        {
            foreach (Placement placement in _placements)
            {
                Scene.Remove(placement.Id);
            }

            _placements.Clear();
            foreach (DrawList2D layer in layers)
            {
                var content = DrawingContent2D.FromDrawList(layer);
                _placements.Add(new(Scene.Insert(_group, _placements.Count, content), content, [layer]));
            }

            _reference = null;
        }
        else
        {
            for (int i = 0; i < layers.Count; i++)
            {
                Placement placement = _placements[i];
                if (ReferenceEquals(placement.Pages[0], layers[i]))
                {
                    continue;
                }

                placement.Pages[0] = layers[i];
                placement.Content = DrawingContent2D.FromDrawList(layers[i]);
                Scene.ReplaceContent(placement.Id, placement.Content);
                _reference = null;
            }
        }
    }

    private sealed class Placement(DrawingNodeId2D id, DrawingContent2D content, DrawList2D[] pages)
    {
        internal DrawingNodeId2D Id { get; } = id;

        internal DrawingContent2D Content { get; set; } = content;

        internal DrawList2D[] Pages { get; } = pages;

        internal Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
    }
}
