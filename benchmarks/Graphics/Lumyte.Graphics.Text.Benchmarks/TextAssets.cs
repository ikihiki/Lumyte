using System.Diagnostics;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed class TextAssets : IDisposable
{
    private readonly List<DistanceFieldText2D> _owned = [];
    private readonly TextLayout2D[] _layouts = new TextLayout2D[2];
    private readonly DistanceFieldText2D?[] _distance = new DistanceFieldText2D?[2];
    private DistanceFieldText2D? _anchor;

    private TextAssets()
    {
    }

    internal List<AtlasSample> Lifecycle { get; } = [];

    internal DrawList2D[] Lists { get; } = new DrawList2D[2];

    public void Dispose()
    {
        for (int index = _owned.Count - 1; index >= 0; index--)
        {
            _owned[index].Dispose();
            _owned.RemoveAt(index);
        }
    }

    internal static async Task<TextAssets> CreateAsync(VulkanDevice device, TextFixture fixture, string method)
    {
        var assets = new TextAssets();
        try
        {
            if (method != "vector")
            {
                assets._anchor = await assets.CreateAtlasAsync(device, fixture.Anchor, method, "analytic-O-anchor", 0);
            }

            for (int state = 0; state < 2; state++)
            {
                if (state == 1 && !fixture.Input.Changes)
                {
                    assets._layouts[1] = assets._layouts[0];
                    assets._distance[1] = assets._distance[0];
                    assets.Lists[1] = assets.Lists[0];
                    assets.Lifecycle.Add(new AtlasSample("state-1-reuses-state-0", 0, null, null, null, null, null, null, null, null, assets._layouts[0].Geometry.Count, assets._layouts[0].Drawings.OfType<TextPaintGeometry2D>().Count()));
                    continue;
                }

                long started = Stopwatch.GetTimestamp();
                assets._layouts[state] = fixture.Layout(state);
                double layoutMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (method != "vector")
                {
                    assets._distance[state] = await assets.CreateAtlasAsync(device, assets._layouts[state], method, $"state-{state}", layoutMs);
                }
                else
                {
                    assets.Lifecycle.Add(new AtlasSample($"state-{state}", layoutMs, null, null, null, null, null, null, null, null, assets._layouts[state].Geometry.Count, assets._layouts[state].Drawings.OfType<TextPaintGeometry2D>().Count()));
                }

                var canvas = new Canvas2D();
                canvas.Begin();
                if (method == "vector")
                {
                    canvas.DrawText(fixture.Anchor, TextPixels.AnchorOrigin);
                    canvas.DrawText(assets._layouts[state], TextPixels.TextOrigin);
                }
                else
                {
                    assets._anchor!.Draw(canvas, TextPixels.AnchorOrigin);
                    assets._distance[state]!.Draw(canvas, TextPixels.TextOrigin);
                }

                assets.Lists[state] = canvas.End();
            }

            return assets;
        }
        catch
        {
            assets.Dispose();
            throw;
        }
    }

    internal DrawList2D VectorReference(TextLayout2D anchor, int state)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        AppendLogicalOutlines(canvas, anchor, TextPixels.AnchorOrigin);
        AppendLogicalOutlines(canvas, _layouts[state], TextPixels.TextOrigin);
        return canvas.End();
    }

    private static void AppendLogicalOutlines(Canvas2D canvas, TextLayout2D layout, Vector2 origin)
    {
        // Independent adapter from logical glyph snapshot; does not call DrawText or flatten DrawList.
        canvas.Save();
        canvas.Translate(origin);
        foreach (TextDrawing2D drawing in layout.Drawings)
        {
            if (drawing is TextGeometry2D outline)
            {
                canvas.FillPath(outline.Path, outline.Paint);
            }
            else if (drawing is TextPaintGeometry2D color)
            {
                canvas.FillRect(color.Bounds, color.Paint);
            }
            else
            {
                throw new NotSupportedException("Unknown logical text drawing.");
            }
        }

        canvas.Restore();
    }

    private async Task<DistanceFieldText2D> CreateAtlasAsync(VulkanDevice device, TextLayout2D layout, string method, string role, double layoutMs)
    {
        DistanceFieldMode2D mode = method == "msdf" ? DistanceFieldMode2D.Msdf : DistanceFieldMode2D.Mtsdf;
        long start = Stopwatch.GetTimestamp();
        DistanceFieldText2D owner = await DistanceFieldText2D.CreateAsync(device, layout, pixelsPerUnit: 1, distanceRange: 8, mode: mode, maximumAtlasDimension: 2048);
        double createMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _owned.Add(owner);
        using var timer = new HardwareGpuTimer(device);
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        start = Stopwatch.GetTimestamp();
        if (timer.Supported)
        {
            timer.Begin(commands);
        }

        owner.RecordUpload(commands);
        if (timer.Supported)
        {
            timer.End(commands);
        }

        commands.Finish();
        double encodeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        using IGraphicsSubmission submission = NativeCompletion.Submit(device, commands);
        double submitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        await NativeCompletion.WaitAsync(submission);
        double waitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double? gpu = timer.Supported ? timer.ReadMilliseconds() : null;
        IGraphicsTexture atlas = owner.View.Texture;
        Lifecycle.Add(new AtlasSample(role, layoutMs, createMs, encodeMs, submitMs, waitMs, gpu, atlas.Width, atlas.Height, (ulong)atlas.Width * atlas.Height * 4, layout.Geometry.Count, layout.Drawings.OfType<TextPaintGeometry2D>().Count()));
        return owner;
    }
}
