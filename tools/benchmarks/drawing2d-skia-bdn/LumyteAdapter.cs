using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;

sealed class LumyteAdapter : IAdapter
{
    readonly VulkanDevice device;
    readonly Renderer2D renderer;
    readonly IGraphicsTexture tex;
    readonly IGraphicsTextureView view;
    readonly Canvas2D canvas = new();
    readonly Trace trace;
    DrawList2D? list;
    bool initialized;
    PreparedDraw2D? pendingPrepared;
    IGraphicsCommandBuffer? pendingCommand;
    IGraphicsSubmission? pendingSubmission;
    IGraphicsBuffer<byte>? pendingReadback;
    bool submitAttempted;
    public string Identity { get; }

    internal LumyteAdapter(Trace trace)
    {
        this.trace = trace;
        if (Environment.GetEnvironmentVariable("VK_DRIVER_FILES") != "/usr/share/vulkan/icd.d/radeon_icd.json")
            throw new InvalidOperationException("Explicit Radeon hardware ICD required.");
        device = VulkanDevice.Create();
        Identity = HardwareIdentity.Verify(device);
        renderer = new(device, DrawingCommandRenderers2D.CreateDefault());
        tex = device.CreateTexture(new() { Width = Trace.W, Height = Trace.H,
            Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        view = tex.CreateView();
    }

    public void Record(int state)
    {
        if (trace.Static && list != null) return;
        list = trace.Record(state, canvas);
    }

    public async Task RenderAsync()
    {
        EnsureNoPending();
        try
        {
            pendingPrepared = await renderer.PrepareAsync(list!, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
            pendingCommand = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            pendingCommand.Barrier(new TextureBarrierDesc { Texture = tex, Range = new(0, 1, 0, 1),
                BeforeState = initialized ? TextureState.ColorAttachment : TextureState.Undefined,
                AfterState = TextureState.ColorAttachment, Before = initialized ? color : default, After = color });
            pendingPrepared.Record(pendingCommand);
            pendingCommand.Finish();
            submitAttempted = true;
            pendingSubmission = device.Queue.Submit([pendingCommand]);
            await pendingSubmission.WaitAsync();
            if (pendingSubmission.Status != SubmissionStatus.Completed)
                throw new InvalidOperationException("Logical submission completion missing.");
            initialized = true;
            RetireOwners();
            if (!trace.Static) list = null;
        }
        catch (Exception error)
        {
            if (!submitAttempted)
            {
                try { RetireOwners(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            }
            // Accepted/uncertain work remains owned until completion or explicit device retirement.
            throw;
        }
    }

    public byte[] Pixels()
    {
        EnsureNoPending();
        try
        {
            pendingReadback = device.CreateBuffer<byte>(new() { Count = Trace.W * Trace.H * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            pendingCommand = device.CreateCommandBuffer(new());
            pendingCommand.Barrier(new TextureBarrierDesc { Texture = tex, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            pendingCommand.CopyTextureToBuffer(new() { Texture = tex, Width = Trace.W, Height = Trace.H }, new() { Buffer = pendingReadback.SliceElements(0, pendingReadback.Count), BytesPerRow = Trace.W * 4, RowsPerImage = Trace.H });
            pendingCommand.Barrier(new BufferBarrierDesc<byte> { Buffer = pendingReadback.SliceElements(0, pendingReadback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            pendingCommand.Barrier(new TextureBarrierDesc { Texture = tex, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.ColorAttachment, Before = new(PipelineStage.Copy, ResourceAccess.CopyRead), After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
            pendingCommand.Finish();
            submitAttempted = true;
            pendingSubmission = device.Queue.Submit([pendingCommand]);
            pendingSubmission.WaitAsync().GetAwaiter().GetResult();
            pendingReadback.MapAsync().GetAwaiter().GetResult();
            byte[] data = new byte[Trace.W * Trace.H * 4];
            pendingReadback.CopyTo(data);
            pendingReadback.Unmap();
            RetireOwners();
            return data;
        }
        catch
        {
            if (!submitAttempted) RetireOwners();
            throw;
        }
    }

    void EnsureNoPending()
    {
        if (pendingCommand is not null || pendingPrepared is not null || pendingSubmission is not null || pendingReadback is not null)
            throw new InvalidOperationException("A previous frame remains owned after failed completion or cleanup.");
    }

    void RetireOwners()
    {
        pendingCommand?.Dispose(); pendingCommand = null;
        pendingPrepared?.Dispose(); pendingPrepared = null;
        if (pendingReadback is not null)
        {
            if (pendingReadback.IsMapped) pendingReadback.Unmap();
            pendingReadback.Dispose(); pendingReadback = null;
        }
        pendingSubmission?.Dispose(); pendingSubmission = null;
        submitAttempted = false;
    }

    public void Dispose()
    {
        if (submitAttempted)
        {
            try { pendingSubmission?.WaitAsync().GetAwaiter().GetResult(); }
            catch { device.WaitForRetirementAsync().GetAwaiter().GetResult(); }
            if (pendingSubmission is null) device.WaitForRetirementAsync().GetAwaiter().GetResult();
        }
        RetireOwners();
        view.Dispose(); tex.Dispose(); renderer.Dispose(); device.Dispose();
    }
}
