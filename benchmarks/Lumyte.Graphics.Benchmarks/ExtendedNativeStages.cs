using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Graphics.Vulkan;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Benchmarks;

internal static class ExtendedNativeStages
{
    internal static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != "/usr/share/vulkan/icd.d/radeon_icd.json")
        {
            throw new InvalidOperationException("Explicit verified Radeon ICD required.");
        }

        using var device = VulkanDevice.Create();
        using var engine = new ParleyTextEngine2D();
        using FileStream font = File.OpenRead("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        string family = engine.AddFont(font);
        var options = new ParleyTextOptions2D { FontFamily = family, FontSize = 24, WrappingLength = 500 };
        TextLayout2D layout = engine.CreateLayout("Hello Lumyte! Vector 012345", options);
        long start = Stopwatch.GetTimestamp();
        using DistanceFieldText2D atlas = await DistanceFieldText2D.CreateAsync(device, layout);
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "atlas-cold", create = Ms(start), width = atlas.View.Texture.Width, height = atlas.View.Texture.Height }));
        using (IGraphicsCommandBuffer upload = device.CreateCommandBuffer(new()))
        {
            start = Stopwatch.GetTimestamp();
            atlas.RecordUpload(upload);
            upload.Finish();
            double record = Ms(start);
            start = Stopwatch.GetTimestamp();
            using IGraphicsSubmission submission = device.Queue.Submit([upload]);
            await submission.WaitAsync();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "atlas-upload", record, completion = Ms(start) }));
        }

        foreach (int size in new[] { 512, 1536 })
        {
            foreach (string workload in new[] { "rect1000", "fallback100", "static-text", "dynamic-text", "mtsdf-text" })
            {
                await DirectAsync(device, engine, options, layout, atlas, size, workload);
            }

            await LayersAsync(device, size);
        }

        await ThroughputAsync(device);
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static DrawList2D Build(Canvas2D canvas, string workload, int frame, ParleyTextEngine2D engine, ParleyTextOptions2D options, TextLayout2D layout, DistanceFieldText2D atlas)
    {
        canvas.Begin();
        if (workload == "dynamic-text")
        {
            canvas.DrawText(engine.CreateLayout($"Hello Lumyte! Vector {frame % 1000000:D6}", options), new(10, 30));
        }
        else if (workload == "static-text")
        {
            canvas.DrawText(layout, new(10, 30));
        }
        else if (workload == "mtsdf-text")
        {
            atlas.Draw(canvas, new(10, 30));
        }
        else if (workload == "fallback100")
        {
            var builder = new PathBuilder2D();
            builder.MoveTo(Vector2.Zero);
            builder.CubicTo(new(0, 30), new(30, 30), new(30, 0));
            builder.Close();
            Path2D path = builder.Build();
            var paint = PaintGraph2D.LinearGradient(Vector2.Zero, new(30, 0), [new(0, new(1, 0, 0, 1)), new(1, new(0, 0, 1, 1))]);
            canvas.ClipPath(path);
            for (int i = 0; i < 100; i++)
            {
                canvas.FillPath(path, paint);
            }
        }
        else
        {
            for (int i = 0; i < 1000; i++)
            {
                canvas.FillRect(new((i % 32) * 15, (i / 32) * 15, 12, 12), new Color2D(0.3f, 0.5f, 0.8f, 1));
            }
        }

        return canvas.End();
    }

    private static async Task DirectAsync(IGraphicDevice device, ParleyTextEngine2D engine, ParleyTextOptions2D options, TextLayout2D layout, DistanceFieldText2D atlas, int size, string workload)
    {
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = (uint)size, Height = (uint)size, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        for (int frame = 0; frame < 150; frame++)
        {
            long bytes = GC.GetTotalAllocatedBytes(false);
            long start = Stopwatch.GetTimestamp();
            DrawList2D list = Build(canvas, workload, frame, engine, options, layout, atlas);
            double build = Ms(start);
            start = Stopwatch.GetTimestamp();
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
            double prepare = Ms(start);
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = frame == 0 ? TextureState.Undefined : TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = frame == 0 ? default : new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
            start = Stopwatch.GetTimestamp();
            prepared.Record(commands);
            commands.Finish();
            double record = Ms(start);
            start = Stopwatch.GetTimestamp();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            double submit = Ms(start);
            await submission.WaitAsync();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "direct", workload, size, frame, phase = frame == 0 ? "cold" : frame < 50 ? "warmup" : "warm", build, prepare, record, submit, completion = Ms(start), allocated = GC.GetTotalAllocatedBytes(false) - bytes }));
        }
    }

    private static async Task LayersAsync(IGraphicDevice device, int size)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.BeginLayer(new() { ContentBounds = new(0, 0, 200, 200), Effects = graph => graph.AppendGaussianBlur(graph.Source, 3, 3), Opacity = 0.8f });
        canvas.FillRoundedRect(new(20, 20, 160, 120), 8, new Color2D(1, 0, 0, 1));
        canvas.EndLayer();
        RenderGraphDrawList2D list = canvas.End();
        for (int frame = 0; frame < 150; frame++)
        {
            using var graph = new Graph(device);
            var desc = new TextureDesc { Width = (uint)size, Height = (uint)size, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
            var target = new RenderTargetBinding2D { Texture = graph.CreateTexture(desc), Description = desc, LoadOp = AttachmentLoadOp.Clear };
            using var renderer = new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), list, target, new DrawingGraphBindings2D(graph));
            long bytes = GC.GetTotalAllocatedBytes(false);
            long start = Stopwatch.GetTimestamp();
            using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
            graph.MarkOutput(target.Texture);
            double prepare = Ms(start);
            start = Stopwatch.GetTimestamp();
            using CompiledRenderGraph plan = graph.Compile();
            double compile = Ms(start);
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            start = Stopwatch.GetTimestamp();
            plan.Record(commands);
            commands.Finish();
            double record = Ms(start);
            start = Stopwatch.GetTimestamp();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "layers-blur", size, frame, phase = frame == 0 ? "cold" : frame < 50 ? "warmup" : "warm", prepare, compile, record, completion = Ms(start), allocated = GC.GetTotalAllocatedBytes(false) - bytes }));
        }
    }

    private static async Task ThroughputAsync(IGraphicDevice device)
    {
        foreach (int depth in new[] { 1, 3 })
        {
            using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
            var workload = new RecordingBenchmarks { Count = 10000 };
            DrawList2D list = workload.Rectangles();
            var slots = new List<Slot>();
            try
            {
                for (int i = 0; i < depth; i++)
                {
                    slots.Add(new Slot(device));
                }

                for (int block = 0; block < 7; block++)
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int frame = 0; frame < 100; frame++)
                    {
                        Slot slot = slots[frame % depth];
                        await slot.RetireAsync();
                        slot.Prepared = await renderer.PrepareAsync(list, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear });
                        slot.Commands = device.CreateCommandBuffer(new());
                        slot.Commands.Barrier(new TextureBarrierDesc { Texture = slot.Texture, Range = new(0, 1, 0, 1), BeforeState = slot.Used ? TextureState.ColorAttachment : TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = slot.Used ? new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) : default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
                        slot.Prepared.Record(slot.Commands);
                        slot.Commands.Finish();
                        slot.Submission = device.Queue.Submit([slot.Commands]);
                        slot.Used = true;
                    }

                    foreach (Slot slot in slots)
                    {
                        await slot.RetireAsync();
                    }

                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "throughput", depth, block, phase = block == 0 ? "warmup" : "warm", frames = 100, elapsed = Ms(start) }));
                }
            }
            finally
            {
                foreach (Slot slot in slots)
                {
                    await slot.RetireAsync();
                    slot.Dispose();
                }
            }
        }
    }

    private sealed class Slot : IDisposable
    {
        internal Slot(IGraphicDevice device)
        {
            Texture = device.CreateTexture(new() { Width = 800, Height = 800, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
            View = Texture.CreateView();
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal PreparedDraw2D? Prepared { get; set; }

        internal IGraphicsCommandBuffer? Commands { get; set; }

        internal IGraphicsSubmission? Submission { get; set; }

        internal bool Used { get; set; }

        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
        }

        internal async Task RetireAsync()
        {
            if (Submission is not null)
            {
                await Submission.WaitAsync();
                Submission.Dispose();
                Submission = null;
            }

            Commands?.Dispose();
            Commands = null;
            Prepared?.Dispose();
            Prepared = null;
        }
    }
}
