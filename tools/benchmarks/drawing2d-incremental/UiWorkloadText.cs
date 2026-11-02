using System.Diagnostics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI;
using Lumyte.UI.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using Panel = Lumyte.UI.Compose.Definitions.Panel;
using Stack = Lumyte.UI.Compose.Definitions.Stack;
using TextBox = Lumyte.UI.TextEditing.Compose.Definitions.TextBox;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Benchmark-only adapter: caches completed immutable UI recordings explicitly.
// This is not a claim that UiSurface or a production host caches OnRecord calls.
internal sealed class UiWorkloadText : IUiTextLayoutProvider, IDisposable
{
    private readonly ParleyTextEngine2D _engine;
    private readonly ParleyUiTextLayoutProvider _provider;

    internal UiWorkloadText()
    {
        string fontPath = FindFont();
        _engine = new ParleyTextEngine2D();
        try
        {
            using Stream font = File.OpenRead(fontPath);
            string family = _engine.AddFont(font);
            _provider = new(_engine, new() { FontFamily = family, FontSize = 17 });
        }
        catch
        {
            _engine.Dispose();
            throw;
        }
    }

    internal long LayoutCalls { get; private set; }

    internal long LayoutTicks { get; private set; }

    public void Dispose() => _engine.Dispose();

    public UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            LayoutCalls++;
            return _provider.CreateLayout(document, maximumWidth);
        }
        finally
        {
            LayoutTicks += Stopwatch.GetTimestamp() - start;
        }
    }

    internal static Stack Column(string key, float width, params Widget[] children)
    {
        var stack = new Stack { Key = key, Width = width, Spacing = 3, ClipToBounds = true };
        stack.Children.Initialize(children);
        return stack;
    }

    internal static Stack Row(string key, params Widget[] children)
    {
        var stack = new Stack { Key = key, Orientation = UiOrientation.Horizontal, Spacing = 8, ClipToBounds = true };
        stack.Children.Initialize(children);
        return stack;
    }

    internal static Panel Backed(string key, Widget content, float width, float height)
    {
        var panel = new Panel { Key = key, Width = width, Height = height, ClipToBounds = true };
        panel.Children.Initialize(new Lumyte.UI.Compose.Definitions.Rectangle { Fill = new(0.18f, 0.21f, 0.27f, 0.95f) }, content);
        return panel;
    }

    internal static DrawList2D Capture(UiSurface surface)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        return canvas.End();
    }

    internal static void VerifyLayers(IReadOnlyList<DrawList2D> layers)
    {
        if (layers.Count == 0 || layers.Any(layer => layer.Commands.Count == 0))
        {
            throw new InvalidOperationException("The real UI fixture must record nonempty layers.");
        }
    }

    internal TextBox Label(string value, string key, float width = 260, bool editable = false) => new() { Key = key, Text = value, LayoutProvider = this, Width = width, Height = 26, IsReadOnly = !editable };

    private static string FindFont()
    {
        string? configured = Environment.GetEnvironmentVariable("WORKLOAD_REPO_ROOT");
        string[] starts = configured is null ? [AppContext.BaseDirectory, Directory.GetCurrentDirectory()] : [configured];
        foreach (string start in starts)
        {
            for (DirectoryInfo? directory = new(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            {
                string[] relativePaths = ["tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf", "samples/Lumyte.Graphics.Text.Parley.Browser.Sample/Fonts/DejaVuSans.ttf"];
                foreach (string relative in relativePaths)
                {
                    string path = Path.Combine(directory.FullName, relative);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
        }

        throw new FileNotFoundException("Repository DejaVuSans.ttf is required. Set WORKLOAD_REPO_ROOT to the source checkout; no OS font fallback is used.");
    }
}
