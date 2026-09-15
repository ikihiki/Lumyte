using Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

// Public raw extension contracts only. This file can be source-linked by generator consumer tests.
internal sealed class ManagerTestBackend : IPortableGpuBackend
{
    internal sealed class Buffer(GpuBufferDescription description) : GpuBufferHandle
    { internal GpuBufferDescription Description { get; } = description; internal byte[] Bytes { get; } = new byte[checked((int)description.Size)]; }
    internal sealed class Texture(GpuTextureDescription description) : GpuTextureHandle
    { internal GpuTextureDescription Description { get; } = description; internal byte[] Bytes { get; } = new byte[checked((int)(description.Width * description.Height * description.Depth * description.LayerCount * FormatBytes(description.Format)))]; }
    private static uint FormatBytes(GpuFormat format) => format switch { GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2, GpuFormat.Rgba16Float => 8, _ => 4 };
    internal sealed class Layout(GpuBindingLayoutEntry[] entries) : GpuBindingLayoutHandle
    { internal GpuBindingLayoutEntry[] Entries { get; } = entries; }
    internal sealed class Bindings(GpuBindingLayoutHandle layout, GpuBindingEntry[] entries) : GpuBindingsHandle
    { internal GpuBindingLayoutHandle Layout { get; } = layout; internal GpuBindingEntry[] Entries { get; } = entries; }
    private sealed class Module : GpuShaderModuleHandle;
    private sealed class Compute : GpuComputePipelineHandle;
    private sealed class Raster : GpuRasterPipelineHandle;
    internal List<object> Created { get; } = [];
    internal List<object> Destroyed { get; } = [];
    internal List<string> Events { get; } = [];
    internal List<Bindings> CreatedBindings { get; } = [];
    internal Dictionary<object, Exception> DestructionErrors { get; } = new(ReferenceEqualityComparer.Instance);
    internal Exception? CreationError { get; set; }
    internal Exception? MappingDisposeError { get; set; }
    internal bool Disposed { get; private set; }
    internal Queue TestQueue { get; }
    internal ManagerTestBackend() { TestQueue = new(this); }
    public GpuBackendCapabilities Capabilities => new(DirectRootData: true, DualSourceBlend: true);
    public GpuDeviceLimits Limits => new() { MaxImmediateSize = 256, MaxBindGroups = 8, MaxBufferSize = ulong.MaxValue };
    public IGpuQueue MainQueue => TestQueue;
    public GpuBufferHandle CreateBuffer(GpuBufferDescription description) => Create(new Buffer(description));
    public GpuTextureHandle CreateTexture(GpuTextureDescription description) => Create(new Texture(description));
    private T Create<T>(T resource) where T : class
    { if (CreationError is not null) { throw CreationError; } Created.Add(resource); return resource; }
    public void DestroyBuffer(GpuBufferHandle buffer) => Destroy(buffer);
    public void DestroyTexture(GpuTextureHandle texture) => Destroy(texture);
    private void Destroy(object resource)
    {
        Events.Add($"Destroy:{resource.GetType().Name}"); Destroyed.Add(resource);
        if (DestructionErrors.TryGetValue(resource, out Exception? error)) { throw error; }
    }
    public ValueTask<GpuMappedBufferRange> MapBufferAsync(GpuBufferHandle buffer, GpuMapMode mode, ulong offset, ulong length)
        => ValueTask.FromResult<GpuMappedBufferRange>(new Mapping(this, ((Buffer)buffer).Bytes.AsMemory(checked((int)offset), checked((int)length)), mode));
    private sealed class Mapping(ManagerTestBackend backend, Memory<byte> memory, GpuMapMode mode) : GpuMappedBufferRange
    {
        private bool disposed;
        public override Memory<byte> Memory { get { ObjectDisposedException.ThrowIf(disposed, this); if (mode != GpuMapMode.Write) { throw new InvalidOperationException(); } return memory; } }
        public override ReadOnlyMemory<byte> ReadOnlyMemory { get { ObjectDisposedException.ThrowIf(disposed, this); return memory; } }
        public override void Dispose()
        {
            if (disposed) { return; }
            backend.Events.Add("Unmap");
            if (backend.MappingDisposeError is { } error) { throw error; }
            disposed = true;
        }
    }
    public GpuBindingLayoutHandle CreateBindingLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries) => Create(new Layout(entries.ToArray()));
    public void DestroyBindingLayout(GpuBindingLayoutHandle layout) => Destroy(layout);
    public GpuBindingsHandle CreateBindings(GpuBindingLayoutHandle layout, ReadOnlySpan<GpuBindingEntry> entries)
    { var result = Create(new Bindings(layout, entries.ToArray())); CreatedBindings.Add(result); return result; }
    public void DestroyBindings(GpuBindingsHandle bindings) => Destroy(bindings);
    public GpuShaderModuleHandle CreateShaderModule(string wgsl) => Create(new Module());
    public void DestroyShaderModule(GpuShaderModuleHandle module) => Destroy(module);
    public GpuComputePipelineHandle CreateComputePipeline(GpuShaderProgramDescription shaders) => Create(new Compute());
    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline) => Destroy(pipeline);
    public GpuRasterPipelineHandle CreateRasterPipeline(GpuRasterPipelineDescription description, GpuShaderProgramDescription shaders) => Create(new Raster());
    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline) => Destroy(pipeline);
    public void Dispose() { Disposed = true; Events.Add("BackendDispose"); }

    internal sealed class Timeline : GpuSemaphore
    {
        internal readonly Dictionary<ulong, TaskCompletionSource> Signals = [];
        internal readonly HashSet<ulong> Ended = [];
        internal bool Disposed;
        public override void Dispose() => Disposed = true;
    }
    internal sealed class Queue(ManagerTestBackend backend) : IGpuQueue
    {
        internal bool AutoComplete { get; set; } = true;
        internal Exception? SubmitError { get; set; }
        internal bool RegisterBeforeThrow { get; set; }
        internal Exception? ObservationError { get; set; }
        internal Action? AfterCompletionRead { get; set; }
        internal List<(Timeline Timeline, ulong Value, Recording[] Recordings)> Submitted { get; } = [];
        internal List<Timeline> Timelines { get; } = [];
        internal List<Recording> Recordings { get; } = [];
        public GpuCommandBuffer StartCommandRecording() { var recording = new Recording(backend); Recordings.Add(recording); return recording; }
        public GpuSemaphore CreateSemaphore(ulong initialValue = 0)
        { var result = new Timeline(); result.Ended.Add(initialValue); Timelines.Add(result); return result; }
        public void Submit(ReadOnlySpan<GpuCommandBuffer> commands, GpuSemaphore semaphore, ulong value)
        {
            if (SubmitError is not null && !RegisterBeforeThrow) { throw SubmitError; }
            var timeline = (Timeline)semaphore;
            timeline.Signals.Add(value, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Submitted.Add((timeline, value, commands.ToArray().Cast<Recording>().ToArray()));
            backend.Events.Add("Submit");
            if (AutoComplete) { Complete(Submitted.Count - 1); }
            if (SubmitError is not null) { throw new Portable.GpuSubmissionException(new(semaphore, value), SubmitError); }
        }
        internal void Complete(int index, params GpuDiagnostic[] diagnostics)
        {
            var submission = Submitted[index];
            foreach (Recording recording in submission.Recordings)
            { foreach (Action operation in recording.Operations) { operation(); } }
            submission.Timeline.Ended.Add(submission.Value);
            if (diagnostics.Length == 0) { submission.Timeline.Signals[submission.Value].TrySetResult(); }
            else { submission.Timeline.Signals[submission.Value].TrySetException(new Portable.GpuExecutionException(new(submission.Timeline, submission.Value), diagnostics)); }
        }
        public bool IsComplete(GpuSemaphore semaphore, ulong value)
        {
            if (ObservationError is not null) { throw ObservationError; }
            var timeline = (Timeline)semaphore;
            if (!timeline.Ended.Contains(value) && !timeline.Signals.ContainsKey(value)) { throw new InvalidOperationException("Point is unobservable."); }
            bool ended = timeline.Ended.Contains(value);
            AfterCompletionRead?.Invoke();
            return ended;
        }
        public ValueTask WaitAsync(GpuSemaphore semaphore, ulong value, CancellationToken cancellationToken = default)
        {
            if (ObservationError is not null) { return ValueTask.FromException(ObservationError); }
            var timeline = (Timeline)semaphore;
            return timeline.Signals.TryGetValue(value, out TaskCompletionSource? signal)
                ? new(signal.Task.WaitAsync(cancellationToken)) : ValueTask.FromException(new InvalidOperationException("Point is unobservable."));
        }
    }
    internal sealed class Recording(ManagerTestBackend backend) : GpuCommandBuffer
    {
        internal List<Action> Operations { get; } = [];
        internal List<(GpuBufferRange Source, GpuBufferRange Destination)> BufferCopies { get; } = [];
        internal List<GpuColorAttachment> ColorAttachments { get; } = [];
        internal bool Disposed { get; private set; }
        internal Exception? DisposalError { get; set; }
        internal int DisposalAttempts { get; private set; }
        public override void BeginRendering(ReadOnlySpan<GpuColorAttachment> colors, GpuDepthStencilAttachment? depthStencil = null)
            => ColorAttachments.AddRange(colors.ToArray());
        public override void EndRendering() { }
        public override void SetPipeline(GpuRasterPipelineHandle pipeline) { }
        public override void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor) { }
        public override void SetStencilReference(uint reference) { }
        public override void SetBlendConstant(GpuClearColor color) { }
        public override void SetBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default) { }
        public override void SetRootData(ReadOnlySpan<byte> bytes) { }
        public override void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) { }
        public override void DrawIndexed(GpuBufferRange indices, GpuIndexFormat format, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) { }
        public override void DrawIndirect(GpuBufferRange arguments) { }
        public override void DrawIndexedIndirect(GpuBufferRange indices, GpuIndexFormat format, GpuBufferRange arguments) { }
        public override void BeginCompute() { }
        public override void EndCompute() { }
        public override void SetComputePipeline(GpuComputePipelineHandle pipeline) { }
        public override void SetComputeBindings(uint group, GpuBindingsHandle bindings, ReadOnlySpan<uint> dynamicOffsets = default) { }
        public override void SetComputeRootData(ReadOnlySpan<byte> bytes) { }
        public override void Dispatch(uint x, uint y = 1, uint z = 1) { }
        public override void DispatchIndirect(GpuBufferRange arguments) { }
        public override void CopyBuffer(GpuBufferRange source, GpuBufferRange destination)
        {
            BufferCopies.Add((source, destination));
            Operations.Add(() => ((Buffer)source.Buffer).Bytes.AsSpan(checked((int)source.Offset), checked((int)source.Length!.Value))
                .CopyTo(((Buffer)destination.Buffer).Bytes.AsSpan(checked((int)destination.Offset))));
        }
        public override void CopyBufferToTexture(GpuBufferRange source, GpuTextureHandle texture, GpuTextureCopyFootprint footprint)
            => Operations.Add(() => Transfer(((Buffer)source.Buffer).Bytes, checked((int)source.Offset), (Texture)texture, footprint, true));
        public override void CopyTextureToBuffer(GpuTextureHandle texture, GpuTextureCopyFootprint footprint, GpuBufferRange destination)
            => Operations.Add(() => Transfer(((Buffer)destination.Buffer).Bytes, checked((int)destination.Offset), (Texture)texture, footprint, false));
        private static void Transfer(byte[] buffer, int offset, Texture texture, GpuTextureCopyFootprint footprint, bool upload)
        {
            int pixelBytes = checked((int)FormatBytes(texture.Description.Format));
            int rowBytes = checked((int)footprint.Extent.Width * pixelBytes);
            int rowPitch = footprint.RowPitch == 0 ? rowBytes : checked((int)footprint.RowPitch);
            int imagePitch = footprint.ImagePitch == 0 ? rowPitch * checked((int)footprint.Extent.Height) : checked((int)footprint.ImagePitch);
            for (int z = 0; z < footprint.Extent.Depth; z++)
            {
            for (int y = 0; y < footprint.Extent.Height; y++)
            {
                Span<byte> linear = buffer.AsSpan(offset + z * imagePitch + y * rowPitch, rowBytes);
                int textureOffset = checked((int)((footprint.Origin.Z + z) * texture.Description.Height * texture.Description.Width
                    + (footprint.Origin.Y + y) * texture.Description.Width + footprint.Origin.X) * pixelBytes);
                Span<byte> texels = texture.Bytes.AsSpan(textureOffset, rowBytes);
                if (upload) { linear.CopyTo(texels); } else { texels.CopyTo(linear); }
            }
            }
        }
        public override void CopyTexture(GpuTextureHandle source, GpuTextureCopyFootprint sourceFootprint, GpuTextureHandle destination, GpuTextureCopyFootprint destinationFootprint)
            => Operations.Add(() =>
            {
                Texture input = (Texture)source, output = (Texture)destination;
                int pixelBytes = checked((int)FormatBytes(input.Description.Format));
                int rowBytes = checked((int)sourceFootprint.Extent.Width * pixelBytes);
                for (uint z = 0; z < sourceFootprint.Extent.Depth; z++)
                {
                    for (uint y = 0; y < sourceFootprint.Extent.Height; y++)
                    {
                        int sourceOffset = checked((int)(((sourceFootprint.Origin.Z + z) * input.Description.Height + sourceFootprint.Origin.Y + y) * input.Description.Width + sourceFootprint.Origin.X) * pixelBytes);
                        int destinationOffset = checked((int)(((destinationFootprint.Origin.Z + z) * output.Description.Height + destinationFootprint.Origin.Y + y) * output.Description.Width + destinationFootprint.Origin.X) * pixelBytes);
                        input.Bytes.AsSpan(sourceOffset, rowBytes).CopyTo(output.Bytes.AsSpan(destinationOffset, rowBytes));
                    }
                }
            });
        public override void Dispose()
        {
            if (Disposed) { return; }
            DisposalAttempts++; backend.Events.Add("RecordingDispose");
            if (DisposalError is { } error) { throw error; }
            Disposed = true;
        }
    }
}
