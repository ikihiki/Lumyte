using System.Runtime.InteropServices;
using Lumyte.Graphics;
using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Native.Resources.Tests;

/// <summary>A deterministic in-memory Native backend for manager and generated-input consumer tests.</summary>
public sealed class TestResourceBackend : INativeGpuBackend
{
    public TestResourceBackend() => Queue = new(this);
    public TestQueue Queue { get; }
    public bool AutoComplete { get; set; } = true;
    public bool ExplicitTransitions { get; set; } = true;
    public bool UnknownSubmission { get; set; }
    public Exception? SubmitError { get; set; }
    public Exception? SemaphoreCreationError { get; set; }
    public bool ThrowAfterHandoff { get; set; }
    public bool NestSubmissionException { get; set; }
    public Exception? RecordingDisposalError { get; set; }
    public Exception? RecordingSetupError { get; set; }
    public Func<object, Exception?>? DestructionError { get; set; }
    public bool Disposed { get; private set; }
    public List<Heap> Heaps { get; } = [];
    public List<Region> Regions { get; } = [];
    public List<Texture> Textures { get; } = [];
    public List<object> Destroyed { get; } = [];
    public List<string> Commands { get; } = [];
    public List<GpuClearColor> ColorClears { get; } = [];
    public List<(float Depth, byte Stencil)> DepthStencilClears { get; } = [];
    public List<(NativeGpuDescriptorHeap Heap, uint Index, object Value)> DescriptorWrites { get; } = [];
    public GpuShaderCodeFormat ShaderCodeFormat => GpuShaderCodeFormat.Dxil;
    public NativeGpuCapabilities Capabilities => new(RawShaderPointers: true, BufferDescriptors: true, ExplicitTextureTransitions: ExplicitTransitions);
    public NativeGpuLimits Limits => new(256, new(65535, 65535, 65535, ulong.MaxValue));
    public NativeGpuQueue MainQueue => Queue;
    public NativeGpuQueue? CopyQueue => null;
    public NativeGpuSemaphore CreateSemaphore(ulong initialValue = 0)
    { if (SemaphoreCreationError is { } error) { throw error; } return new Timeline(initialValue); }
    public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind) => new(Align(size, 64), 64, new Compatibility());
    public NativeGpuMemoryRequirements GetTextureMemoryRequirements(NativeGpuTextureDescription description, NativeGpuMemoryKind kind)
        => new(Align((ulong)description.Width * description.Height * description.Depth * description.LayerCount * 4, 256), 256, new Compatibility());
    public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind, ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
    { Heap heap = new(size, alignment, kind); Heaps.Add(heap); return heap; }
    public void DestroyGpuHeap(NativeGpuHeap heap) { Destroy(heap); ((Heap)heap).Release(); }
    public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset)
    { Region region = new((Heap)heap, offset, size); Regions.Add(region); return region; }
    public void DestroyLinearRegion(NativeGpuLinearRegion region) => Destroy(region);
    public NativeGpuTextureHandle CreateTexture(NativeGpuTextureDescription description, NativeGpuHeap heap, ulong offset)
    { Texture texture = new(description, (Heap)heap, offset); Textures.Add(texture); return texture; }
    public void DestroyTexture(NativeGpuTextureHandle texture) => Destroy(texture);
    public NativeGpuRenderViewHandle CreateRenderView(NativeGpuTextureView view, NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None) => new RenderView(flags);
    public void DestroyRenderView(NativeGpuRenderViewHandle view) => Destroy(view);
    public NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity) => new DescriptorHeap(kind, capacity);
    public void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap) => Destroy(heap);
    public void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuTextureView view, NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled)
        => DescriptorWrites.Add((heap, index, view));
    public void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuRange range, NativeGpuBufferAccess access)
        => DescriptorWrites.Add((heap, index, range));
    public void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description)
        => DescriptorWrites.Add((heap, index, description));
    public NativeGpuRasterPipelineHandle CreateRasterPipeline(NativeGpuRasterPipelineDescription description, NativeGpuShaderProgram program) => throw new NotSupportedException();
    public void DestroyRasterPipeline(NativeGpuRasterPipelineHandle pipeline) => throw new NotSupportedException();
    public NativeGpuComputePipelineHandle CreateComputePipeline(NativeGpuShaderProgram program) => throw new NotSupportedException();
    public void DestroyComputePipeline(NativeGpuComputePipelineHandle pipeline) => throw new NotSupportedException();
    private void Destroy(object resource)
    { Destroyed.Add(resource); if (DestructionError?.Invoke(resource) is { } error) { throw error; } }
    public void Dispose() { Disposed = true; foreach (Heap heap in Heaps) { heap.Release(); } }
    public void CompleteAll() => Queue.CompleteAll();
    public static byte[] Read(NativeGpuRange range)
    {
        byte[] data = new byte[checked((int)range.Size)];
        Region region = (Region)range.Region;
        Marshal.Copy(checked(region.Storage.Pointer + (nint)(region.HeapOffset + range.Offset)), data, 0, data.Length);
        return data;
    }
    private static ulong Align(ulong value, ulong alignment) => checked((value + alignment - 1) / alignment * alignment);
    private sealed class Compatibility : NativeGpuMemoryCompatibility;
    public sealed class Heap : NativeGpuHeap
    {
        public Heap(ulong size, ulong alignment, NativeGpuMemoryKind kind) : base(size, alignment, kind)
        { Pointer = Marshal.AllocHGlobal(checked((nint)size)); }
        public nint Pointer { get; private set; }
        public void Release() { if (Pointer != 0) { Marshal.FreeHGlobal(Pointer); Pointer = 0; } }
    }
    public sealed class Region(Heap heap, ulong offset, ulong size) : NativeGpuLinearRegion(heap, offset, size,
        checked(0x100000ul + offset), heap.Kind == NativeGpuMemoryKind.GpuOnly ? 0 : checked(heap.Pointer + (nint)offset))
    { public Heap Storage { get; } = heap; }
    public sealed class Texture(NativeGpuTextureDescription description, Heap heap, ulong offset) : NativeGpuTextureHandle
    {
        public NativeGpuTextureDescription Description { get; } = description;
        public Heap Heap { get; } = heap;
        public ulong Offset { get; } = offset;
        public byte[] Data { get; } = new byte[checked((int)(description.Width * description.Height * description.Depth * description.LayerCount * 4))];
    }
    private sealed class RenderView(NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags);
    private sealed class DescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity) : NativeGpuDescriptorHeap(kind, capacity);
    public sealed class Timeline(ulong initialValue) : NativeGpuSemaphore
    {
        private ulong completed = initialValue;
        private readonly List<(ulong Value, TaskCompletionSource Completion)> waits = [];
        public bool Disposed { get; private set; }
        public override bool IsComplete(ulong value) => completed >= value;
        public override void WaitCpu(ulong value) => throw new InvalidOperationException("Manager code must never block the calling thread.");
        public override ValueTask WaitAsync(ulong value, CancellationToken cancellationToken = default)
        {
            if (completed >= value) { return ValueTask.CompletedTask; }
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            waits.Add((value, completion));
            return new(completion.Task.WaitAsync(cancellationToken));
        }
        public override void SignalCpu(ulong value)
        {
            completed = Math.Max(completed, value);
            foreach (var wait in waits.Where(wait => wait.Value <= completed).ToArray()) { wait.Completion.TrySetResult(); waits.Remove(wait); }
        }
        public override void Dispose() => Disposed = true;
    }
    public sealed class TestQueue(TestResourceBackend owner) : NativeGpuQueue
    {
        private readonly List<NativeGpuTimelinePoint> pending = [];
        public int SubmitCount { get; private set; }
        public override NativeGpuCommandBuffer StartCommandRecording() => new Recording(owner);
        public override void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuTimelinePoint signal, ReadOnlySpan<NativeGpuTimelinePoint> waits = default)
        {
            SubmitCount++;
            if (owner.SubmitError is { } error && !owner.ThrowAfterHandoff)
            {
                if (owner.UnknownSubmission)
                {
                    Exception wrapped = new NativeGpuSubmissionException(signal, error);
                    throw owner.NestSubmissionException ? new AggregateException(new InvalidOperationException("Wrapped backend failure.", wrapped)) : wrapped;
                }
                throw error;
            }
            foreach (NativeGpuCommandBuffer command in commands) { ((Recording)command).Execute(); }
            if (owner.AutoComplete) { signal.Semaphore.SignalCpu(signal.Value); } else { pending.Add(signal); }
            if (owner.SubmitError is { } lateError)
            {
                if (owner.UnknownSubmission) { throw new NativeGpuSubmissionException(signal, lateError); }
                throw lateError;
            }
        }
        internal void CompleteAll()
        { foreach (NativeGpuTimelinePoint point in pending) { point.Semaphore.SignalCpu(point.Value); } pending.Clear(); }
    }
    private sealed class Recording(TestResourceBackend owner) : NativeGpuCommandBuffer
    {
        private readonly List<Action> operations = [];
        private bool disposed;
        internal void Execute() { foreach (Action operation in operations) { operation(); } }
        public override void Dispose()
        {
            if (!disposed)
            {
                disposed = true; owner.Commands.Add("DisposeRecording");
                if (owner.RecordingDisposalError is { } error) { throw error; }
            }
        }
        public override void SetResourceDescriptorHeap(NativeGpuDescriptorHeap heap)
        {
            owner.Commands.Add("ResourceHeap");
            if (owner.RecordingSetupError is { } error) { throw error; }
        }
        public override void SetSamplerDescriptorHeap(NativeGpuDescriptorHeap heap) => owner.Commands.Add("SamplerHeap");
        public override void CopyMemory(NativeGpuRange source, NativeGpuRange destination)
        {
            owner.Commands.Add("CopyBuffer");
            operations.Add(() =>
            {
                byte[] data = Read(source); Region region = (Region)destination.Region;
                Marshal.Copy(data, 0, checked(region.Storage.Pointer + (nint)(region.HeapOffset + destination.Offset)), data.Length);
            });
        }
        public override void CopyMemoryToTexture(NativeGpuRange source, NativeGpuTextureHandle destination, NativeGpuTextureCopyFootprint footprint)
        {
            owner.Commands.Add("CopyTexture");
            operations.Add(() =>
            {
                byte[] data = Read(source); Texture texture = (Texture)destination;
                int rowBytes = checked((int)footprint.Extent.Width * 4);
                for (uint image = 0; image < Math.Max(footprint.LayerCount, footprint.Extent.Depth); image++)
                {
                    for (uint row = 0; row < footprint.Extent.Height; row++)
                    {
                        int target = checked((int)(((image + footprint.BaseLayer) * texture.Description.Height + row + footprint.Origin.Y) * texture.Description.Width + footprint.Origin.X) * 4);
                        data.AsSpan(checked((int)(image * footprint.ImagePitch + row * footprint.RowPitch)), rowBytes).CopyTo(texture.Data.AsSpan(target, rowBytes));
                    }
                }
            });
        }
        public override void CopyTextureToMemory(NativeGpuTextureHandle source, NativeGpuRange destination, NativeGpuTextureCopyFootprint footprint)
        {
            owner.Commands.Add("ReadTexture");
            operations.Add(() =>
            {
                Texture texture = (Texture)source; Region region = (Region)destination.Region;
                int rowBytes = checked((int)footprint.Extent.Width * 4);
                for (uint image = 0; image < Math.Max(footprint.LayerCount, footprint.Extent.Depth); image++)
                {
                    for (uint row = 0; row < footprint.Extent.Height; row++)
                    {
                        int sourceOffset = checked((int)(((image + footprint.BaseLayer) * texture.Description.Height + row + footprint.Origin.Y) * texture.Description.Width + footprint.Origin.X) * 4);
                        nint target = checked(region.Storage.Pointer + (nint)(region.HeapOffset + destination.Offset + image * footprint.ImagePitch + row * footprint.RowPitch));
                        Marshal.Copy(texture.Data, sourceOffset, target, rowBytes);
                    }
                }
            });
        }
        public override void Barrier(GpuStage beforeStages, GpuAccess beforeAccess, GpuStage afterStages, GpuAccess afterAccess) => owner.Commands.Add("Barrier");
        public override void TextureTransition(NativeGpuTextureView view, GpuTextureLayout beforeLayout, GpuTextureLayout afterLayout) => owner.Commands.Add($"Transition:{afterLayout}");
        public override void DiscardTexture(NativeGpuTextureView view, GpuTextureLayout afterLayout) => owner.Commands.Add($"Discard:{afterLayout}");
        public override void SetPipeline(NativeGpuRasterPipelineHandle pipeline) { }
        public override void SetDepthStencilState(NativeGpuDepthStencilState state) { }
        public override void SetViewport(NativeGpuViewport viewport) { }
        public override void SetScissor(NativeGpuScissorRect scissor) { }
        public override void BeginRendering(ReadOnlySpan<NativeGpuColorAttachment> colorAttachments, NativeGpuDepthStencilAttachment? depthStencilAttachment = null)
        {
            foreach (NativeGpuColorAttachment attachment in colorAttachments)
            { if (attachment.LoadOp == NativeGpuLoadOp.Clear) { owner.ColorClears.Add(attachment.ClearColor); } }
            if (depthStencilAttachment is { DepthLoadOp: NativeGpuLoadOp.Clear } depth)
            { owner.DepthStencilClears.Add((depth.ClearDepth, depth.ClearStencil)); }
        }
        public override void EndRendering() { }
        public override void Draw(ReadOnlySpan<byte> rootData, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) { }
        public override void DrawIndexed(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) { }
        public override void DrawIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments) { }
        public override void DrawIndexedIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange indices, NativeGpuIndexFormat format, NativeGpuRange arguments) { }
        public override void DispatchMesh(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1) { }
        public override void DispatchMeshIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments) { }
        public override void SetComputePipeline(NativeGpuComputePipelineHandle pipeline) { }
        public override void Dispatch(ReadOnlySpan<byte> rootData, uint x, uint y = 1, uint z = 1) { }
        public override void DispatchIndirect(ReadOnlySpan<byte> rootData, NativeGpuRange arguments) { }
    }
}
