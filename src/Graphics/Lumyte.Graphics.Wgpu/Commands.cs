using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

public sealed unsafe class CommandEncoder : IDisposable
{
    internal WgpuDevice Owner { get; }
    private WGPUCommandEncoderImpl* _handle;
    private HashSet<GpuResource> _resources = [];
    private RenderEncoder? _render;
    private bool _finished;
    internal CommandEncoder(WgpuDevice owner, WGPUCommandEncoderImpl* handle)
    { Owner = owner; _handle = handle; owner.EncoderCount++; }
    internal void Active()
    {
        Owner.Check();
        if (_handle == null || _finished) throw new InvalidOperationException("Encoder is no longer recording.");
    }
    private void OutsidePass()
    {
        Active();
        if (_render is not null) throw new InvalidOperationException("End the render pass before this operation.");
    }
    internal void Use(GpuResource resource)
    { resource.Check(Owner); if (_resources.Add(resource)) resource.Acquire(); }
    internal void EndPass(RenderEncoder render)
    { if (!ReferenceEquals(_render, render)) throw new InvalidOperationException("Pass is not active."); _render = null; }

    public RenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        lock (Owner.Gate)
        {
            OutsidePass(); ArgumentNullException.ThrowIfNull(desc); ArgumentNullException.ThrowIfNull(desc.Target); desc.Target.Check(Owner);
            if (!Enum.IsDefined(desc.Load) || !Enum.IsDefined(desc.Store)) throw new ArgumentException("Invalid load/store operation.");
            var c = desc.ClearValue;
            if (!double.IsFinite(c.R) || !double.IsFinite(c.G) || !double.IsFinite(c.B) || !double.IsFinite(c.A)) throw new ArgumentException("Clear value must be finite.");
            var attachment = new WGPURenderPassColorAttachment {
                view = desc.Target.Native.Handle, depthSlice = uint.MaxValue,
                loadOp = desc.Load == LoadOp.Clear ? WGPULoadOp.Clear : WGPULoadOp.Load,
                storeOp = desc.Store == StoreOp.Store ? WGPUStoreOp.Store : WGPUStoreOp.Discard,
                clearValue = new WGPUColor { r = c.R, g = c.G, b = c.B, a = c.A },
            };
            var native = new WGPURenderPassDescriptor {
                label = new WGPUStringView { length = nuint.MaxValue }, colorAttachmentCount = 1, colorAttachments = &attachment,
            };
            var handle = WGPU.wgpuCommandEncoderBeginRenderPass(_handle, &native);
            Use(desc.Target);
            _render = new(this, handle, desc.Target.Texture.Width, desc.Target.Texture.Height);
            return _render;
        }
    }
    public void Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y = 1, uint z = 1)
    {
        lock (Owner.Gate)
        {
            OutsidePass(); pipeline.Check(Owner); arguments.Check(Owner);
            if (!ReferenceEquals(arguments.Pipeline, pipeline)) throw new ArgumentException("Arguments belong to another pipeline.");
            var max = Owner.Native.GetLimits().maxComputeWorkgroupsPerDimension;
            if (x > max || y > max || z > max) throw new ArgumentOutOfRangeException(nameof(x));
            Use(pipeline); Use(arguments);
            var pass = WGPU.wgpuCommandEncoderBeginComputePass(_handle, null);
            try
            {
                WGPU.wgpuComputePassEncoderSetPipeline(pass, pipeline.Native.Handle);
                WGPU.wgpuComputePassEncoderSetBindGroup(pass, 0, arguments.Handle, 0, null);
                WGPU.wgpuComputePassEncoderDispatchWorkgroups(pass, x, y, z);
                WGPU.wgpuComputePassEncoderEnd(pass);
            }
            finally { WGPU.wgpuComputePassEncoderRelease(pass); }
        }
    }
    public void CopyBuffer(BufferSlice source, BufferSlice destination)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            if (source.Buffer is null || destination.Buffer is null) throw new ArgumentException("Invalid buffer slice.");
            source.Buffer.Check(Owner); destination.Buffer.Check(Owner);
            if (ReferenceEquals(source.Buffer, destination.Buffer) || source.Length != destination.Length ||
                source.Offset % 4 != 0 || destination.Offset % 4 != 0 || source.Length % 4 != 0 ||
                !source.Buffer.Usage.HasFlag(BufferUsage.CopySource) || !destination.Buffer.Usage.HasFlag(BufferUsage.CopyDestination))
                throw new ArgumentException("Copy requires distinct buffers, matching aligned ranges and copy usages.");
            Use(source.Buffer); Use(destination.Buffer);
            WGPU.wgpuCommandEncoderCopyBufferToBuffer(_handle, source.Buffer.Native.Handle, source.Offset,
                destination.Buffer.Native.Handle, destination.Offset, source.Length);
        }
    }
    public void CopyTextureToBuffer(Texture source, Buffer destination, uint bytesPerRow)
    {
        lock (Owner.Gate)
        {
            OutsidePass(); source.Check(Owner); destination.Check(Owner);
            ulong required = checked((ulong)bytesPerRow * (source.Height - 1) + (ulong)source.Width * 4);
            if (bytesPerRow % 256 != 0 || bytesPerRow < (ulong)source.Width * 4 || required > destination.SizeInBytes || !destination.Usage.HasFlag(BufferUsage.CopyDestination))
                throw new ArgumentException("Texture copy requires aligned rows and sufficient CopyDestination storage.");
            Use(source); Use(destination);
            var src = new WGPUTexelCopyTextureInfo { texture = source.Native.Handle, aspect = WGPUTextureAspect.All };
            var dst = new WGPUTexelCopyBufferInfo { buffer = destination.Native.Handle,
                layout = new WGPUTexelCopyBufferLayout { bytesPerRow = bytesPerRow, rowsPerImage = source.Height } };
            var extent = new WGPUExtent3D { width = source.Width, height = source.Height, depthOrArrayLayers = 1 };
            WGPU.wgpuCommandEncoderCopyTextureToBuffer(_handle, &src, &dst, &extent);
        }
    }
    public CommandBuffer Finish()
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            var handle = WGPU.wgpuCommandEncoderFinish(_handle, null);
            if (handle == null) throw new InvalidOperationException("Command buffer creation failed.");
            _finished = true; WGPU.wgpuCommandEncoderRelease(_handle); _handle = null; Owner.EncoderCount--;
            var resources = _resources; _resources = [];
            return new(Owner, handle, resources);
        }
    }
    public void Dispose()
    {
        lock (Owner.Gate)
        {
            if (_handle == null) return;
            _render?.Abort(); _render = null;
            WGPU.wgpuCommandEncoderRelease(_handle); _handle = null; Owner.EncoderCount--;
            foreach (var resource in _resources) resource.ReleaseLease();
            _resources.Clear();
        }
    }
}

public sealed unsafe class RenderEncoder : IDisposable
{
    private readonly CommandEncoder _parent;
    private WGPURenderPassEncoderImpl* _handle;
    private readonly uint _width, _height;
    private GraphicsPipeline? _pipeline;
    private BufferSlice _indices;
    private IndexFormat _indexFormat;
    internal RenderEncoder(CommandEncoder parent, WGPURenderPassEncoderImpl* handle, uint width, uint height)
    {
        _parent = parent; _handle = handle; _width = width; _height = height;
        WGPU.wgpuRenderPassEncoderSetViewport(handle, 0, 0, width, height, 0, 1);
        WGPU.wgpuRenderPassEncoderSetScissorRect(handle, 0, 0, width, height);
    }
    private void Active()
    { _parent.Active(); if (_handle == null) throw new InvalidOperationException("Render pass has ended."); }
    public void SetPipeline(GraphicsPipeline pipeline)
    {
        lock (_parent.Owner.Gate) { Active(); pipeline.Check(_parent.Owner); _parent.Use(pipeline);
            WGPU.wgpuRenderPassEncoderSetPipeline(_handle, pipeline.Native.Handle); _pipeline = pipeline; }
    }
    public void SetViewport(Viewport v)
    {
        lock (_parent.Owner.Gate)
        {
            Active();
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Width) || !float.IsFinite(v.Height) ||
                !float.IsFinite(v.MinDepth) || !float.IsFinite(v.MaxDepth) || v.X < 0 || v.Y < 0 || v.Width <= 0 || v.Height <= 0 ||
                v.X + (double)v.Width > _width || v.Y + (double)v.Height > _height || v.MinDepth < 0 || v.MinDepth > v.MaxDepth || v.MaxDepth > 1)
                throw new ArgumentOutOfRangeException(nameof(v));
            WGPU.wgpuRenderPassEncoderSetViewport(_handle, v.X, v.Y, v.Width, v.Height, v.MinDepth, v.MaxDepth);
        }
    }
    public void SetScissor(Scissor s)
    {
        lock (_parent.Owner.Gate)
        {
            Active(); if ((ulong)s.X + s.Width > _width || (ulong)s.Y + s.Height > _height) throw new ArgumentOutOfRangeException(nameof(s));
            WGPU.wgpuRenderPassEncoderSetScissorRect(_handle, s.X, s.Y, s.Width, s.Height);
        }
    }
    public void SetIndexBuffer(BufferSlice indices, IndexFormat format)
    {
        lock (_parent.Owner.Gate)
        {
            Active(); if (indices.Buffer is null) throw new ArgumentException("Invalid index range.");
            indices.Buffer.Check(_parent.Owner); if (!Enum.IsDefined(format)) throw new ArgumentException("Unknown index format.");
            uint size = format == IndexFormat.Uint16 ? 2u : 4u;
            if (!indices.Buffer.Usage.HasFlag(BufferUsage.Index) || indices.Offset % size != 0 || indices.Length % size != 0)
                throw new ArgumentException("Index range or usage is invalid.");
            _parent.Use(indices.Buffer);
            WGPU.wgpuRenderPassEncoderSetIndexBuffer(_handle, indices.Buffer.Native.Handle,
                format == IndexFormat.Uint16 ? WGPUIndexFormat.Uint16 : WGPUIndexFormat.Uint32, indices.Offset, indices.Length);
            _indices = indices; _indexFormat = format;
        }
    }
    public void Draw(uint vertexCount, uint instanceCount = 1) => Draw(new DrawDesc { VertexCount = vertexCount, InstanceCount = instanceCount });
    public void Draw(DrawDesc desc)
    {
        lock (_parent.Owner.Gate)
        {
            Active(); ArgumentNullException.ThrowIfNull(desc);
            if (_pipeline is null) throw new InvalidOperationException("Set a graphics pipeline before drawing.");
            checked { _ = desc.FirstVertex + desc.VertexCount; _ = desc.FirstInstance + desc.InstanceCount; }
            WGPU.wgpuRenderPassEncoderDraw(_handle, desc.VertexCount, desc.InstanceCount, desc.FirstVertex, desc.FirstInstance);
        }
    }
    public void DrawIndexed(IndexedDrawDesc desc)
    {
        lock (_parent.Owner.Gate)
        {
            Active(); ArgumentNullException.ThrowIfNull(desc);
            if (_pipeline is null || _indices.Buffer is null) throw new InvalidOperationException("Set a pipeline and index buffer before drawing.");
            uint size = _indexFormat == IndexFormat.Uint16 ? 2u : 4u;
            if (((ulong)desc.FirstIndex + desc.IndexCount) * size > _indices.Length) throw new ArgumentOutOfRangeException(nameof(desc));
            checked { _ = desc.FirstInstance + desc.InstanceCount; }
            WGPU.wgpuRenderPassEncoderDrawIndexed(_handle, desc.IndexCount, desc.InstanceCount, desc.FirstIndex, desc.BaseVertex, desc.FirstInstance);
        }
    }
    public void End()
    { lock (_parent.Owner.Gate) { Active(); Abort(); _parent.EndPass(this); } }
    internal void Abort()
    { if (_handle == null) return; WGPU.wgpuRenderPassEncoderEnd(_handle); WGPU.wgpuRenderPassEncoderRelease(_handle); _handle = null; }
    public void Dispose()
    { lock (_parent.Owner.Gate) { if (_handle != null) End(); } }
}

public sealed unsafe class CommandBuffer : GpuResource
{
    internal WGPUCommandBufferImpl* Handle { get; }
    private HashSet<GpuResource> _resources;
    private bool _submitted;
    internal CommandBuffer(WgpuDevice owner, WGPUCommandBufferImpl* handle, HashSet<GpuResource> resources) : base(owner)
    { Handle = handle; _resources = resources; }
    internal void MarkSubmitted()
    { if (_submitted) throw new InvalidOperationException("Command buffer was already submitted."); _submitted = true; }
    internal void CheckUnsubmitted() { if (_submitted) throw new InvalidOperationException("Command buffer was already submitted."); }
    internal HashSet<GpuResource> TakeResources() { var resources = _resources; _resources = []; return resources; }
    protected override void ReleaseNative()
    { WGPU.wgpuCommandBufferRelease(Handle); foreach (var resource in _resources) resource.ReleaseLease(); _resources.Clear(); }
}

public sealed class Submission
{
    private readonly WgpuDevice _owner;
    private readonly A.QueueWorkDoneRequest _request;
    private readonly HashSet<GpuResource> _resources;
    private bool _completed;
    private Exception? _error;
    internal Submission(WgpuDevice owner, A.QueueWorkDoneRequest request, HashSet<GpuResource> resources)
    { _owner = owner; _request = request; _resources = resources; owner.EncoderCount++; }
    public bool IsCompleted
    {
        get
        {
            lock (_owner.Gate)
            {
                if (_completed) return true;
                _owner.Instance.ProcessEvents();
                if (!_request.IsComplete) return false;
                if (!_request.IsSuccess) _error = new InvalidOperationException($"Queue completion failed: {_request.Status}");
                try { _owner.Check(); } catch (Exception ex) { _error = ex; }
                _request.Dispose();
                foreach (var resource in _resources) resource.ReleaseLease();
                _resources.Clear(); _completed = true; _owner.EncoderCount--;
                return true;
            }
        }
    }
    public void Wait(CancellationToken cancellationToken = default)
    {
        while (!IsCompleted) { cancellationToken.ThrowIfCancellationRequested(); Thread.Sleep(1); }
        if (_error is not null) throw _error;
    }
    public async ValueTask WaitAsync(CancellationToken cancellationToken = default)
    {
        while (!IsCompleted) await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        if (_error is not null) throw _error;
    }
}
