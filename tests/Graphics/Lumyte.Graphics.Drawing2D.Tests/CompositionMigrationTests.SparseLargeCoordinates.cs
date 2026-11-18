using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Preserves authoritative CPU coverage when large logical edges cancel into a small target.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>The exact reported rectangle must retain all four fully covered physical pixels.</summary>
    /// <param name="graphRoute">Whether to use the normal parent Graph adapter.</param>
    /// <returns>The completed public preparation and payload assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SparseLargeCoordinatePublicCoverageKeepsFourPixelsAsync(bool graphRoute)
    {
        var device = new ProfilingDevice();
        using RendererBuildHost2D host = SparseBoundaryDefinition(16 * 1024 * 1024, 0).CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(new(4, 0, 0, 1, -67108864, 0));
        canvas.FillRect(new(16777216, 0, 1, 1), SRgba<float>.Red);
        DrawList2D list = canvas.End();
        var description = new TextureDesc { Width = 8, Height = 2, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        SparseStripPlan2D plan = SparseStripBuilder2D.Create(list, new() { Description = description });
        SparseStrip2D cpu = Assert.Single(plan.Strips);
        Assert.Equal(0, cpu.X);
        Assert.Equal(4, cpu.Length);
        Assert.Equal(-1, cpu.MaskOffset);
        if (graphRoute)
        {
            var graphCanvas = new RenderGraphCanvas2D();
            graphCanvas.Begin();
            graphCanvas.Replay(list);
            using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device, "large-coordinate");
            using var graphTexture = new DispatchTexture { Width = 8, Height = 2, Format = TextureFormat.Rgba16Float };
            RenderGraphTexture target = graph.ImportTexture(graphTexture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            var bindings = new DrawingGraphBindings2D(graph);
            bindings.BindTexture(graphTexture, target);
            using var configured = new RenderGraphRenderer2D(renderer, graphCanvas.End(), new() { Texture = target, Description = description }, bindings);
            using PreparedDrawingPasses2D owner = await graph.Append2D(configured);
            using CompiledRenderGraph compiled = graph.Compile();
            using var commands = new ProfilingCommands();
            compiled.Record(commands);
            commands.Dispose();
        }
        else
        {
            using var texture = new DispatchTexture { Width = 8, Height = 2, Format = TextureFormat.Rgba16Float };
            using IGraphicsTextureView view = texture.CreateView();
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view });
        }

        Assert.True(device.NativeBuffers > 0);
        DrawRecord2D record = Assert.Single(device.Records);
        Assert.Equal(new Vector4(0, 0, 4, 1), record.DeviceBounds);
        for (int x = 0; x < 8; x++)
        {
            Assert.Equal(x < 4, x >= record.DeviceBounds.X && x < record.DeviceBounds.Z);
        }
    }

    /// <summary>Public payloads preserve authoritative sample bits and exact target/clip restrictions.</summary>
    /// <param name="graphRoute">Selects the public Graph entry point.</param>
    /// <param name="scenario">Cancellation, reflection, DPR, origin or clipping case.</param>
    /// <returns>The completed all-pixel/all-sample verification.</returns>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(true, 9)]
    [InlineData(true, 10)]
    [InlineData(false, 11)]
    [InlineData(true, 11)]
    public async Task SparseLargeCoordinatePublicSamplesMatchAnalyticIntervalsAsync(bool graphRoute, int scenario)
    {
        var device = new ProfilingDevice { CaptureSparsePayload = true };
        using RendererBuildHost2D host = SparseBoundaryDefinition(16 * 1024 * 1024, 0).CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, Resources(device), null, null);
        float localX = scenario is 1 or 3 ? -16777216 : 16777216;
        float scaleX = scenario is 2 or 3 ? -4 : 4;
        float translation = scenario switch
        {
            1 => 67108864,
            2 => 67108872,
            3 => -67108856,
            7 => -67108880,
            9 => -50331648,
            10 => -83886080,
            _ => -67108864,
        };
        float dpr = scenario is 4 or 11 ? 2 : 1;
        Vector2 origin = new(scenario == 9 ? 16777216 : scenario == 10 ? -16777216 : 0, 0);
        var canvas = new Canvas2D();
        canvas.Begin();
        if (scenario == 5)
        {
            canvas.ClipRect(new(0.5f, 0, 3, 1));
        }
        else if (scenario == 6)
        {
            canvas.ClipRect(new(6, 0, 1, 1));
        }
        else if (scenario == 8)
        {
            canvas.ClipRect(new(0, 0, 8, 4));
        }
        else if (scenario == 11)
        {
            canvas.ClipRect(new(0.25f, 0, 3.5f, 1));
        }

        canvas.ConcatTransform(new(scaleX, 0, 0, 1, translation, 0));
        canvas.FillRect(new(localX, 0, 1, 1), SRgba<float>.Red);
        DrawList2D list = canvas.End();
        var description = new TextureDesc { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        if (graphRoute)
        {
            using var texture = new DispatchTexture { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float };
            using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device, "coordinate-samples");
            RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            var bindings = new DrawingGraphBindings2D(graph);
            bindings.BindTexture(texture, handle);
            var graphCanvas = new RenderGraphCanvas2D();
            graphCanvas.Begin();
            graphCanvas.Replay(list);
            using var configured = new RenderGraphRenderer2D(renderer, graphCanvas.End(), new() { Texture = handle, Description = description, PixelScale = dpr, PixelOrigin = origin }, bindings);
            using PreparedDrawingPasses2D owner = await graph.Append2D(configured);
            using CompiledRenderGraph compiled = graph.Compile();
            using var commands = new ProfilingCommands();
            compiled.Record(commands);
            commands.Dispose();
        }
        else
        {
            using var texture = new DispatchTexture { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float };
            using IGraphicsTextureView view = texture.CreateView();
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, PixelScale = dpr });
        }

        // Expected physical intervals are exact dyadic arithmetic, independent of
        // the production lowerer, DrawingBounds/GetScissor and clip normalizer.
        double left = scenario is 2 or 3 ? 4 : scenario == 7 ? -16 : 0;
        double right = left + (4 * dpr);
        double clipLeft = scenario == 5 ? 0.5 : scenario == 6 ? 6 : scenario == 11 ? 0.5 : 0;
        double clipRight = scenario == 5 ? 3.5 : scenario == 6 ? 7 : scenario == 11 ? 7.5 : 8;
        int expectedPixels = 0;
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                uint expected = 0;
                for (int sample = 0; sample < 16; sample++)
                {
                    double px = x + (((sample % 4) + 0.5) / 4);
                    double py = y + (((sample / 4) + 0.5) / 4);
                    if (px >= left && px < right && py < dpr && px >= clipLeft && px < clipRight)
                    {
                        expected |= 1u << sample;
                    }
                }

                if (expected != 0)
                {
                    expectedPixels++;
                }

                Assert.Equal(expected, SparseUploadedSampleBits(device, x, y));
            }
        }

        if (expectedPixels == 0)
        {
            Assert.Empty(device.Records);
            Assert.Equal(0, device.NativeBuffers);
        }
        else
        {
            Assert.True(device.NativeBuffers > 0);
            Assert.All(device.Records, record =>
            {
                Assert.InRange(record.DeviceBounds.X, 0, 8);
                Assert.InRange(record.DeviceBounds.Z, 0, 8);
                Assert.InRange(record.DeviceBounds.Y, 0, 4);
                Assert.InRange(record.DeviceBounds.W, 0, 4);
            });
        }
    }

    private static uint SparseUploadedSampleBits(ProfilingDevice device, int x, int y)
    {
        uint result = 0;
        foreach (DrawRecord2D record in device.Records)
        {
            if (x < record.DeviceBounds.X || x >= record.DeviceBounds.Z || y < record.DeviceBounds.Y || y >= record.DeviceBounds.W)
            {
                continue;
            }

            Assert.Equal(9u, record.Kind);
            Vector4[] numeric = Assert.Single(device.NumericUploads);
            Assert.Equal(2, device.DirectoryUploads.Count);
            DrawDirectoryEntry2D entry = device.DirectoryUploads[1][checked((int)record.ClipId)];
            int header = checked((int)entry.PayloadOffset16);
            Vector4 bounds = numeric[header];
            if (entry.Kind == 3)
            {
                for (int sample = 0; sample < 16; sample++)
                {
                    int sx = (x * 4) + (sample % 4), sy = (y * 4) + (sample / 4);
                    if (sx >= BitConverter.SingleToInt32Bits(bounds.X) && sx < BitConverter.SingleToInt32Bits(bounds.Z)
                        && sy >= BitConverter.SingleToInt32Bits(bounds.Y) && sy < BitConverter.SingleToInt32Bits(bounds.W))
                    {
                        result |= 1u << sample;
                    }
                }
            }
            else
            {
                Assert.Equal(4u, entry.Kind);
                int offset = BitConverter.SingleToInt32Bits(numeric[header + 1].X);
                int cell = ((y - BitConverter.SingleToInt32Bits(bounds.Y)) * BitConverter.SingleToInt32Bits(bounds.Z))
                    + x - BitConverter.SingleToInt32Bits(bounds.X);
                result |= (uint)numeric[offset + (cell / 4)][cell % 4];
            }
        }

        return result;
    }
}
