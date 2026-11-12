using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

public class ClipFrameBenchmarks
{
    [Params("rect-control", "mask-dynamic", "mask-cached")]
    public string Case { get; set; } = "rect-control";
    VulkanDevice device = null!;
    Renderer2D renderer = null!;
    IGraphicsTexture texture = null!;
    IGraphicsTextureView view = null!;
    DrawingScene2D scene = null!;
    DrawingNodeId2D node;
    int state;
    bool initialized;
    bool submitAttempted;
    PreparedDrawingFrame2D? pending;
    IGraphicsCommandBuffer? commands;
    IGraphicsSubmission? submission;

    [GlobalSetup]
    public async Task Setup()
    {
        device = VulkanDevice.Create();
        Console.WriteLine(HardwareIdentity.Verify(device));
        renderer = new(device, DrawingCommandRenderers2D.CreateDefault());
        texture = device.CreateTexture(new() { Width = 256, Height = 192, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        view = texture.CreateView();
        scene = new();
        node = scene.Insert(scene.Root, 0, Record(0));
        await CompletedFrameAsync();
        Console.WriteLine($"case={Case};256x192;128 rectangles;setup completed; dynamic includes recording+CPU-mask creation; cached reuses completion-published prepared asset");
    }

    DrawList2D Record(int frame)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (Case != "rect-control")
        {
            float shift = (frame & 15) * 0.03125f;
            var triangle = new PathBuilder2D();
            triangle.MoveTo(new(8.2f + shift, 7.3f));
            triangle.LineTo(new(190.1f, 10.4f + shift));
            triangle.LineTo(new(16.3f + shift, 170.2f));
            triangle.Close();
            canvas.ClipPath(triangle.Build());
            canvas.ClipRect(new(10.2f, 8.3f, 175.6f, 155.4f));
        }
        for (int i = 0; i < 128; i++)
            canvas.FillRect(new((i % 16) * 14, (i / 16) * 21, 18, 24), new Color2D(1, 0.25f, 0.5f, 0.75f));
        return canvas.End();
    }

    [Benchmark]
    public async Task CompletedFrameAsync()
    {
        if (Case != "mask-cached") scene.ReplaceContent(node, Record(++state));
        DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
        using (DrawingSceneReadLease2D read = scene.Borrow())
            pending = await renderer.PrepareAsync(read, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
        if (initialized && Case == "mask-cached" && (after.ReusedRuns <= before.ReusedRuns || after.CompiledRuns != before.CompiledRuns))
            throw new InvalidOperationException("Static cached fixture did not reuse its prepared mask asset");
        if (initialized && Case != "mask-cached" && after.CompiledRuns <= before.CompiledRuns)
            throw new InvalidOperationException("Dynamic fixture did not compile fresh prepared work");
        commands = device.CreateCommandBuffer(new());
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = initialized ? TextureState.ColorAttachment : TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = initialized ? attachment : default, After = attachment });
        pending.Record(commands);
        commands.Finish();
        submitAttempted = true;
        submission = device.Queue.Submit([commands]);
        pending.AttachSubmission(submission);
        await submission.WaitAsync();
        if (submission.Status != SubmissionStatus.Completed) throw new InvalidOperationException("GPU completion missing");
        initialized = true;
        commands.Dispose(); commands = null;
        pending.Retire(); pending.Dispose(); pending = null;
        submission.Dispose(); submission = null;
        submitAttempted = false;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (submitAttempted && (submission == null || submission.Status != SubmissionStatus.Completed))
        {
            try { submission?.WaitAsync().GetAwaiter().GetResult(); }
            catch { device.WaitForRetirementAsync().GetAwaiter().GetResult(); }
            if (submission == null || submission.Status != SubmissionStatus.Completed || device.Status != GraphicsDeviceStatus.Ready)
            {
                device.WaitForRetirementAsync().GetAwaiter().GetResult();
                commands?.Dispose(); commands = null;
                Console.Error.WriteLine("Failed benchmark quarantines its recorded frame; no unsubmitted assertion or residency publication. Process exit owns final quarantine teardown.");
                return;
            }
        }
        if (submission != null)
        {
            submission.WaitAsync().GetAwaiter().GetResult();
            commands?.Dispose(); commands = null;
            pending?.Retire();
        }
        else if (commands != null)
        {
            commands.Dispose(); commands = null;
            pending?.AbortUnsubmitted();
        }
        pending?.Dispose(); submission?.Dispose();
        scene?.Dispose(); view?.Dispose(); texture?.Dispose(); renderer?.Dispose(); device?.Dispose();
    }
}
