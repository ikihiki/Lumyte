using Lumyte.Graphics;
using A = Ahjo.Wgpu;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal abstract class GpuResource : IDisposable
{
    internal WgpuDevice Owner { get; }
    private bool _disposed;
    private int _leases;
    internal GpuResource(WgpuDevice owner) { Owner = owner; owner.ResourceCount++; }
    internal void Check(WgpuDevice owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(owner, Owner)) throw new ArgumentException("Resource belongs to another device.");
        owner.Check();
    }
    internal void Acquire() { Check(Owner); _leases++; }
    internal void ReleaseLease() => _leases--;
    internal void RequireIdle()
    {
        Check(Owner);
        if (_leases != 0) throw new InvalidOperationException("Resource is referenced by recorded or in-flight GPU work.");
    }
    public void Dispose()
    {
        lock (Owner.Gate)
        {
            if (_disposed) return;
            if (_leases != 0) throw new InvalidOperationException("Resource is referenced by recorded or in-flight GPU work.");
            ReleaseNative();
            _disposed = true;
            Owner.ResourceCount--;
        }
    }
    protected abstract void ReleaseNative();
}

internal class WgpuBuffer : GpuResource, IBufferBackendContract
{
    internal A.Buffer Native { get; }
    public ulong SizeInBytes { get; }
    public BufferUsage Usage { get; }
    public MemoryPreference Memory { get; }
    internal WgpuBuffer(WgpuDevice owner, A.Buffer native, ulong size, BufferUsage usage, MemoryPreference memory) : base(owner)
        => (Native, SizeInBytes, Usage, Memory) = (native, size, usage, memory);
    public BufferSlice Slice(ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
                throw new ArgumentOutOfRangeException(nameof(length));
            return new(this, offset, length);
        }
    }
    public void ValidateRange(ulong offset, ulong length) => Slice(offset, length);
    public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            ValidateRange(offset, length); RequireIdle();
            if (Memory != MemoryPreference.Upload || SizeInBytes > int.MaxValue || (ulong)source.Length > length)
                throw new ArgumentException("CPU copy requires an idle Upload buffer and a valid managed-size range.");
            var status = Native.MapBlocking(Owner.Instance, A.MapMode.Write, 0, (nuint)SizeInBytes);
            if (status != WGPUMapAsyncStatus.Success) throw new InvalidOperationException($"WgpuBuffer map failed: {status}");
            try
            {
                Owner.CheckErrors();
                source.CopyTo(Native.GetMappedRange<byte>(0, (nuint)SizeInBytes).Slice(checked((int)offset), source.Length));
            }
            finally { Native.Unmap(); }
        }
    }
    public void CopyTo(Span<byte> destination, ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            ValidateRange(offset, length); RequireIdle();
            if (Memory != MemoryPreference.Readback || SizeInBytes > int.MaxValue || length > (ulong)destination.Length)
                throw new ArgumentException("CPU copy requires completed Readback memory and a sufficient destination.");
            var status = Native.MapBlocking(Owner.Instance, A.MapMode.Read, 0, (nuint)SizeInBytes);
            if (status != WGPUMapAsyncStatus.Success) throw new InvalidOperationException($"WgpuBuffer map failed: {status}");
            try
            {
                Owner.CheckErrors();
                Native.GetConstMappedRange<byte>(0, (nuint)SizeInBytes).Slice(checked((int)offset), checked((int)length)).CopyTo(destination);
            }
            finally { Native.Unmap(); }
        }
    }
    protected override void ReleaseNative() => Native.Dispose();
}

// The public typed interface and the internal allocation contract are the same object.
internal sealed class WgpuBuffer<T> : WgpuBuffer, IGraphicsBuffer<T> where T : unmanaged
{
    public ulong Count { get; }
    public BufferLayout<T> Layout { get; }
    internal WgpuBuffer(WgpuDevice owner, A.Buffer native, BufferDesc<T> desc, BufferLayout<T> layout)
        : base(owner, native, layout.GetSizeInBytes(desc.Count), desc.Usage, desc.Memory) => (Count, Layout) = (desc.Count, layout);
    public new Lumyte.Graphics.BufferSlice<T> Slice(ulong offset, ulong count)
    {
        if (count == 0 || offset > Count || count > Count - offset)
            throw new ArgumentOutOfRangeException(nameof(count));
        ValidateRange(Layout.GetSizeInBytes(offset), Layout.GetSizeInBytes(count));
        return new(this, offset, count);
    }
    public void CopyFrom(ReadOnlySpan<T> source) => base.CopyFrom(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source), 0, SizeInBytes);
    public void CopyTo(Span<T> destination) => base.CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(destination), 0, SizeInBytes);
}

internal sealed class Texture : GpuResource, IGraphicsTexture, ITextureBackendContract
{
    internal A.Texture Native { get; }
    public uint Width { get; }
    public uint Height { get; }
    internal Texture(WgpuDevice owner, A.Texture native, TextureDesc desc) : base(owner)
        => (Native, Width, Height) = (native, desc.Width, desc.Height);
    public IGraphicsTextureView CreateView()
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            return new TextureView(this, Owner.Validated(Native.CreateView()));
        }
    }
    protected override void ReleaseNative() => Native.Dispose();
}

internal sealed class TextureView : GpuResource, IGraphicsTextureView, ITextureViewBackendContract
{
    internal A.TextureView Native { get; }
    internal Texture Texture { get; }
    IGraphicsTexture IGraphicsTextureView.Texture => Texture;
    ITextureBackendContract ITextureViewBackendContract.Texture => Texture;
    internal TextureView(Texture texture, A.TextureView native) : base(texture.Owner)
    { Texture = texture; Native = native; texture.Acquire(); }
    protected override void ReleaseNative() { Native.Dispose(); Texture.ReleaseLease(); }
}

internal sealed class ShaderModule : GpuResource
{
    internal A.ShaderModule Native { get; }
    internal ShaderModule(WgpuDevice owner, A.ShaderModule native) : base(owner) => Native = native;
    protected override void ReleaseNative() => Native.Dispose();
}

internal sealed class GraphicsPipeline : GpuResource
{
    internal A.RenderPipeline Native { get; }
    internal GraphicsPipeline(WgpuDevice owner, A.RenderPipeline native) : base(owner) => Native = native;
    protected override void ReleaseNative() => Native.Dispose();
}

internal sealed class ComputePipeline : GpuResource
{
    internal A.ComputePipeline Native { get; }
    internal ComputePipeline(WgpuDevice owner, A.ComputePipeline native) : base(owner) => Native = native;
    public unsafe ShaderArguments CreateArguments(GpuReference<uint> data)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            var slice = data.Data;
            if (slice.Buffer is null) throw new ArgumentException("Reference was not created by a device.");
            slice.Buffer.Check(Owner);
            if (!slice.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite)) throw new ArgumentException("ShaderWrite usage is required.");
            var limits = Owner.Native.GetLimits();
            if (slice.Offset % limits.minStorageBufferOffsetAlignment != 0 || slice.Length > limits.maxStorageBufferBindingSize)
                throw new ArgumentException("Storage buffer reference does not satisfy device limits.");
            // The first implementation has one logical RWStructuredBuffer<uint> argument.
            // Its binding is library-owned; callers pass only the opaque data reference.
            var layout = WGPU.wgpuComputePipelineGetBindGroupLayout(Native.Handle, 0);
            try
            {
                var entry = new WGPUBindGroupEntry { binding = 0, buffer = slice.Buffer.Native.Handle, offset = slice.Offset, size = slice.Length };
                var desc = new WGPUBindGroupDescriptor {
                    label = new WGPUStringView { length = nuint.MaxValue }, layout = layout, entryCount = 1, entries = &entry };
                var handle = WGPU.wgpuDeviceCreateBindGroup(Owner.Native.Handle, &desc);
                try { Owner.CheckErrors(); } catch { if (handle != null) WGPU.wgpuBindGroupRelease(handle); throw; }
                if (handle == null) throw new InvalidOperationException("Bind group creation failed.");
                return new ShaderArguments(this, slice.Buffer, handle);
            }
            finally { WGPU.wgpuBindGroupLayoutRelease(layout); }
        }
    }
    protected override void ReleaseNative() => Native.Dispose();
}

internal sealed unsafe class ShaderArguments : GpuResource
{
    internal WGPUBindGroupImpl* Handle { get; }
    internal ComputePipeline Pipeline { get; }
    private readonly WgpuBuffer _buffer;
    internal ShaderArguments(ComputePipeline pipeline, WgpuBuffer buffer, WGPUBindGroupImpl* handle) : base(pipeline.Owner)
    { Pipeline = pipeline; _buffer = buffer; Handle = handle; pipeline.Acquire(); buffer.Acquire(); }
    protected override void ReleaseNative()
    { WGPU.wgpuBindGroupRelease(Handle); Pipeline.ReleaseLease(); _buffer.ReleaseLease(); }
}
