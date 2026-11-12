using System.Diagnostics.Tracing;
using System.Diagnostics;
using System.Text.Json;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

public class RecordFrameBenchmarks
{
    [Params("rect1000-dynamic", "rect1000-cached", "rect341-dynamic", "rect342-dynamic", "path200-dynamic", "path200-cached", "mixed200-dynamic", "mixed200-cached")]
    public string Case { get; set; } = "rect1000-dynamic";
    VulkanDevice device = null!;
    Renderer2D renderer = null!;
    IGraphicsTexture texture = null!;
    IGraphicsTextureView view = null!;
    DrawingScene2D scene = null!;
    DrawingNodeId2D node;
    int state;
    Trace trace = null!;
    readonly Guid profilingRun = Guid.NewGuid();
    EventSource? drawingProvider;
    long profilingFrame;
    bool initialized;
    public bool Diagnostics { get; set; }
    public int LastRecordedState { get; private set; }
    public (uint First, uint Count)? RangeSelection { get; set; }
    bool submitAttempted;
    PreparedDrawingFrame2D? pending;
    IGraphicsCommandBuffer? commands;
    IGraphicsSubmission? submission;

    [GlobalSetup]
    public async Task Setup()
    {
        trace = new Trace(Case.StartsWith("path") ? "path-dynamic" : "rect-dynamic");
        device = VulkanDevice.Create();
        Console.WriteLine(HardwareIdentity.Verify(device));
        renderer = new(device, DrawingCommandRenderers2D.CreateDefault());
        texture = device.CreateTexture(new() { Width = 1920, Height = 1080, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        view = texture.CreateView();
        scene = new();
        node = scene.Insert(scene.Root, 0, Record(0));
        using (Activity setup = StartDiagnosticFrame(-1)) await CompletedFrameAsync();
        drawingProvider = EventSource.GetSources().FirstOrDefault(source => source.Name == "Lumyte-Drawing2D-Profiling");
        Console.WriteLine($"case={Case};1920x1080;completed+command-disposed+retired; dynamic includes record/CPU-clip/pack/transfers; cached reuses published prepared GPU assets");
    }

    DrawList2D Record(int frame)
    {
        LastRecordedState = frame;
        if (Case.StartsWith("path") || Case.StartsWith("rect1000")) return trace.Record(frame, new Canvas2D());
        var canvas = new Canvas2D();
        canvas.Begin();
        if (!Case.StartsWith("mixed"))
        {
            int count = Case.StartsWith("rect341") ? 341 : Case.StartsWith("rect342") ? 342 : 1000;
            for (int i = 0; i < count; i++)
            {
                Op operation = trace.Frames[frame & 31][i];
                canvas.Save();
                canvas.ConcatTransform(operation.Transform);
                canvas.FillRect(operation.Rect, trace.Paints[operation.Paint]);
                canvas.Restore();
            }
        }
        else
        {
            float shift = (frame & 3) * 0.125f;
            var clip = new PathBuilder2D();
            clip.MoveTo(new(0.25f + shift, 0.375f)); clip.LineTo(new(88, 0.5f)); clip.LineTo(new(0.5f, 64)); clip.Close();
            canvas.ClipPath(clip.Build());
            canvas.ClipRect(new(1.25f, 1.375f, 80.5f, 60.25f));
            var paint = PaintGraph2D.LinearGradient(new(0, 0), new(88, 64), [new(0, new(1, 0, 0, 0.75f)), new(0.5f, new(0, 1, 0, 0.5f)), new(1, new(0, 0, 1, 1))]);
            for (int i = 0; i < 200; i++)
            {
                float x = (i % 20) * 3.5f + shift, y = (i / 20) * 4;
                if (i % 3 == 0) canvas.FillRect(new(x, y, 18, 20), paint);
                else if (i % 3 == 1) canvas.FillCircle(new(x + 8, y + 8), 10, paint);
                else
                {
                    var path = new PathBuilder2D();
                    path.MoveTo(new(x, y)); path.CubicTo(new(x + 16, y - 5), new(x - 4, y + 22), new(x + 18, y + 20)); path.LineTo(new(x, y + 18)); path.Close();
                    canvas.FillPath(path.Build(), paint);
                }
            }
        }
        return canvas.End();
    }

    [Benchmark]
    public async Task CompletedFrameAsync()
    {
        using Activity? diagnostic = drawingProvider?.IsEnabled() == true ? StartDiagnosticFrame(++profilingFrame) : null;
        long start = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        if (!Case.EndsWith("cached")) scene.ReplaceContent(node, Record(++state));
        long recorded = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
        using (DrawingSceneReadLease2D read = scene.Borrow())
            pending = await renderer.PrepareAsync(read, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
        if (initialized && Case.EndsWith("cached") && (after.ReusedRuns <= before.ReusedRuns || after.CompiledRuns != before.CompiledRuns))
            throw new InvalidOperationException("Static cached fixture did not reuse its prepared mask asset");
        if (initialized && !Case.EndsWith("cached") && after.CompiledRuns <= before.CompiledRuns)
            throw new InvalidOperationException("Dynamic fixture did not compile fresh prepared work");
        long prepared = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        commands = device.CreateCommandBuffer(new());
        RecordDiagnosticCommands? observer = Diagnostics ? new(commands, RangeSelection) : null;
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = initialized ? TextureState.ColorAttachment : TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = initialized ? attachment : default, After = attachment });
        pending.Record(observer ?? commands);
        commands.Finish();
        long encoded = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        submitAttempted = true;
        submission = device.Queue.Submit([commands]);
        pending.AttachSubmission(submission);
        long submitted = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        await submission.WaitAsync();
        if (submission.Status != SubmissionStatus.Completed) throw new InvalidOperationException("GPU completion missing");
        long completed = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        var payload = observer?.Inspect();
        long retireStarted = Diagnostics ? Stopwatch.GetTimestamp() : 0;
        initialized = true;
        commands.Dispose(); commands = null;
        pending.Retire(); pending.Dispose(); pending = null;
        submission.Dispose(); submission = null;
        submitAttempted = false;
        if (Diagnostics) Console.WriteLine(JsonSerializer.Serialize(new { Case, recordUs = Stopwatch.GetElapsedTime(start, recorded).TotalMicroseconds, prepareUs = Stopwatch.GetElapsedTime(recorded, prepared).TotalMicroseconds, encodeUs = Stopwatch.GetElapsedTime(prepared, encoded).TotalMicroseconds, submitUs = Stopwatch.GetElapsedTime(encoded, submitted).TotalMicroseconds, waitUs = Stopwatch.GetElapsedTime(submitted, completed).TotalMicroseconds, inspectionUs = Stopwatch.GetElapsedTime(completed, retireStarted).TotalMicroseconds, disposeRetireUs = Stopwatch.GetElapsedTime(retireStarted).TotalMicroseconds, preparedDeviceBufferCount = payload?.Buffers, preparedBoundBufferBytes = payload?.BoundBytes, preparedDeviceBufferBackingBytes = payload?.BackingBytes, observer!.Draws, observer.Instances, observer.RenderPasses, pipelineCount = observer.Pipelines.Count, observer.Copies, observer.CopyBytes, roots = observer.Roots.GroupBy(root => root).ToDictionary(group => group.Key, group => group.Count()), cachedBytes = renderer.RetentionStatistics.CachedBytes }));
    }

    Activity StartDiagnosticFrame(long frame)
    {
        Activity activity = new Activity(frame < 0 ? "record-bdn-setup" : "record-bdn-extra-frame").Start();
        activity.SetCustomProperty("Lumyte.Profiling.RunId", profilingRun);
        activity.SetCustomProperty("Lumyte.Profiling.FrameId", frame);
        return activity;
    }

    public async Task<byte[]> PixelsAsync()
    {
        uint rowBytes = 1920 * 4;
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
        uint pitch = (rowBytes + alignment - 1) / alignment * alignment;
        using var readback = device.CreateBuffer<byte>(new() { Count = pitch * 1080, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var copy = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        copy.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        copy.CopyTextureToBuffer(new() { Texture = texture, Width = 1920, Height = 1080 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 1080 });
        copy.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        copy.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.ColorAttachment, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = color });
        copy.Finish();
        using var copied = device.Queue.Submit([copy]);
        try { await copied.WaitAsync(); }
        catch { await device.WaitForRetirementAsync(); throw; }
        await readback.MapAsync();
        byte[] raw = new byte[checked((int)readback.Count)];
        readback.CopyTo(raw); readback.Unmap();
        if (pitch == rowBytes) return raw;
        byte[] tight = new byte[1920 * 1080 * 4];
        for (int y = 0; y < 1080; y++) raw.AsSpan(checked((int)(y * pitch)), (int)rowBytes).CopyTo(tight.AsSpan(y * (int)rowBytes));
        return tight;
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
