using System.Runtime.CompilerServices;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>Common resource ownership over one Native manager. Native interoperability remains in this provider assembly.</summary>
public sealed partial class NativeGraphResources : IGpuGraphResources
{
    private readonly ConditionalWeakTable<GpuResourceRef, GpuGraphResourceRef> wrappers = new();
    private readonly ConditionalWeakTable<GpuGraphPackageRef, GpuPackageRef> packages = new();
    private readonly Guid runtimeId;
    internal readonly object Sync;
    internal readonly SemaphoreSlim Work;
    private int activeOperations;
    private bool closing;
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public GpuResourceManager Manager { get; }
    internal NativeGraphResources(Guid runtimeId, GpuResourceManager manager, object sync, SemaphoreSlim work)
    { this.runtimeId = runtimeId; Manager = manager; Sync = sync; Work = work; }
    public GpuGraphResourceScope CreateScope() { lock (Sync) { RequireOpen(); return new Scope(this, Manager.CreateScope()); } }
    public GpuGraphResourcePin Pin(GpuGraphResourceRef reference)
    { lock (Sync) { RequireOpen(); return new PinLease(this, Manager.Pin(Resolve(reference))); } }
    public IDisposable AcquireUse(GpuGraphResourceRef reference)
    { lock (Sync) { RequireOpen(); return new Lease(this, Manager.AcquireUse(Resolve(reference))); } }
    public void Collect() { lock (Sync) { Manager.Collect(); } }
    public void Trim() { lock (Sync) { Manager.Trim(); } }
    internal IDisposable BeginOperation()
    { lock (Sync) { RequireOpen(); activeOperations++; return new Operation(this); } }
    internal Task CloseAsync()
    { lock (Sync) { closing = true; if (activeOperations == 0) { drained.TrySetResult(); } return drained.Task; } }
    private void RequireOpen() => ObjectDisposedException.ThrowIf(closing, this);
    private sealed class Operation(NativeGraphResources owner) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock (owner.Sync)
            {
                if (disposed) { return; }
                disposed = true; owner.activeOperations--;
                if (owner.closing && owner.activeOperations == 0) { owner.drained.TrySetResult(); }
            }
        }
    }
    public GpuTextureRef GetNativeTexture(GpuGraphTextureRef reference) => (GpuTextureRef)Resolve(reference);
    public GpuBufferRef GetNativeBuffer(GpuGraphBufferRef reference) => (GpuBufferRef)Resolve(reference);
    internal GpuResourceRef Resolve(GpuGraphResourceRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.RuntimeId != runtimeId) { throw new ArgumentException("The resource belongs to another runtime.", nameof(reference)); }
        return reference switch
        {
            TextureRef texture => texture.Value,
            BufferRef buffer => buffer.Value,
            GpuGraphPackageRef package when packages.TryGetValue(package, out GpuPackageRef? value) => value,
            _ => throw new ArgumentException("The reference was not created by this Native runtime.", nameof(reference)),
        };
    }
    internal GpuGraphResourceRef Wrap(GpuResourceRef reference, GpuRenderGraphResource resource) => resource switch
    {
        GpuRenderGraphTexture texture => Wrap((GpuTextureRef)reference, texture.Description),
        GpuRenderGraphBuffer buffer => Wrap((GpuBufferRef)reference, buffer.Description),
        _ => throw new ArgumentException("Only memory resources can be exported.", nameof(resource)),
    };
    internal GpuGraphTextureRef Wrap(GpuTextureRef reference, GpuGraphTextureDescription description)
        => (GpuGraphTextureRef)wrappers.GetValue(reference, value => new TextureRef(runtimeId, (GpuTextureRef)value, description));
    internal GpuGraphBufferRef Wrap(GpuBufferRef reference, GpuGraphBufferDescription description)
        => (GpuGraphBufferRef)wrappers.GetValue(reference, value => new BufferRef(runtimeId, (GpuBufferRef)value, description));
    internal IDisposable Own(IDisposable value) => new Lease(this, value);
    internal static NativeGpuTextureDescription Describe(GpuGraphTextureDescription description, NativeGpuTextureUsage usage = NativeGpuTextureUsage.None)
        => new(description.Dimension switch
        {
            GpuGraphTextureDimension.OneD => NativeGpuTextureDimension.OneD,
            GpuGraphTextureDimension.ThreeD => NativeGpuTextureDimension.ThreeD,
            _ => NativeGpuTextureDimension.TwoD,
        }, description.Width, description.Height,
        description.Dimension == GpuGraphTextureDimension.ThreeD ? description.DepthOrArrayLayers : 1,
        description.MipLevelCount, description.Dimension == GpuGraphTextureDimension.ThreeD ? 1 : description.DepthOrArrayLayers,
        description.SampleCount, description.Format, usage);

    private sealed class TextureRef(Guid runtimeId, GpuTextureRef value, GpuGraphTextureDescription description)
        : GpuGraphTextureRef(runtimeId, Guid.NewGuid(), description)
    { internal GpuTextureRef Value { get; } = value; }
    private sealed class BufferRef(Guid runtimeId, GpuBufferRef value, GpuGraphBufferDescription description)
        : GpuGraphBufferRef(runtimeId, Guid.NewGuid(), description)
    { internal GpuBufferRef Value { get; } = value; }
    private sealed class Lease(NativeGraphResources owner, IDisposable value) : IDisposable
    {
        private IDisposable? current = value;
        public void Dispose() { lock (owner.Sync) { current?.Dispose(); current = null; } }
    }
    private sealed class PinLease(NativeGraphResources owner, GpuResourcePin value) : GpuGraphResourcePin
    {
        private GpuResourcePin? current = value;
        public override void Dispose() { lock (owner.Sync) { current?.Dispose(); current = null; } }
    }
    private sealed class Scope(NativeGraphResources owner, GpuResourceScope value) : GpuGraphResourceScope
    {
        private bool closed;
        private int imports;
        private readonly List<GpuResourceRef> pendingReleases = [];
        public override ValueTask<GpuGraphPackageRef> ImportPackageAsync(GpuPackageUploadData data, CancellationToken cancellationToken = default)
        {
            IDisposable operation;
            lock (owner.Sync) { ObjectDisposedException.ThrowIf(closed, this); operation = owner.BeginOperation(); imports++; }
            return ImportCoreAsync(data, operation, cancellationToken);
        }
        private async ValueTask<GpuGraphPackageRef> ImportCoreAsync(GpuPackageUploadData data, IDisposable operation, CancellationToken cancellationToken)
        {
            try
            {
                GpuGraphPackageRef result = await owner.ImportPackageAsync(value, data, cancellationToken).ConfigureAwait(false);
                lock (owner.Sync) { ObjectDisposedException.ThrowIf(closed, this); return result; }
            }
            finally
            {
                try
                {
                    lock (owner.Sync)
                    {
                        imports--;
                        if (imports == 0)
                        {
                            if (closed) { value.Dispose(); }
                            else { foreach (GpuResourceRef reference in pendingReleases) { value.Release(reference); } }
                            pendingReleases.Clear();
                        }
                    }
                }
                finally { operation.Dispose(); }
            }
        }
        public override void Release(GpuGraphResourceRef reference)
        {
            lock (owner.Sync)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                GpuResourceRef native = owner.Resolve(reference);
                if (imports != 0) { pendingReleases.Add(native); } else { value.Release(native); }
            }
        }
        public override void Dispose()
        { lock (owner.Sync) { if (closed) { return; } closed = true; if (imports == 0) { value.Dispose(); } } }
    }
}
