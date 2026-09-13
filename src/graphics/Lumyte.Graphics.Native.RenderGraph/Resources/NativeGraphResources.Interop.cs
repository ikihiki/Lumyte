using System.Runtime.CompilerServices;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>Owns a Native interoperability import. GPU readers keep its lifetime lease after owner disposal.</summary>
public sealed class NativeGraphResourceImport<TReference> : IDisposable where TReference : GpuGraphResourceRef
{
    private IDisposable? owner;
    internal NativeGraphResourceImport(TReference reference, IDisposable owner) { Reference = reference; this.owner = owner; }
    public TReference Reference { get; }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Dispose();
}

public sealed partial class NativeGraphResources
{
    private readonly ConditionalWeakTable<GpuGraphResourceRef, Task> readiness = new();
    /// <summary>Imports raw Native memory from this backend. The lease owns all required native lifetime dependencies.</summary>
    public NativeGraphResourceImport<GpuGraphBufferRef> ImportBuffer(NativeGpuRange range, IDisposable lease,
        GpuSubmissionToken preceding = default)
    {
        lock (Sync)
        {
            RequireOpen(); ArgumentNullException.ThrowIfNull(lease);
            Task ready = RequirePreceding(preceding);
            GpuResourceScope scope = Manager.CreateScope();
            try
            {
                GpuBufferRef native = scope.ImportBuffer(range, lease);
                if (preceding.IsValid) { Manager.AcquireUse(native).Retire(preceding); }
                GpuGraphBufferRef reference = Wrap(native, new(range.Size)); readiness.Add(reference, ready);
                return new(reference, Own(scope));
            }
            catch { scope.Dispose(); throw; }
        }
    }
    /// <summary>Imports a raw Native texture in General layout; each graph execution returns it to General.</summary>
    /// <remarks>The caller describes the actual resource, declares any accepted main-queue predecessor, and transfers its lifetime lease.</remarks>
    public NativeGraphResourceImport<GpuGraphTextureRef> ImportTexture(NativeGpuTextureHandle texture,
        NativeGpuTextureDescription description, IDisposable lease, GpuSubmissionToken preceding = default)
    {
        lock (Sync)
        {
            RequireOpen(); ArgumentNullException.ThrowIfNull(texture); ArgumentNullException.ThrowIfNull(lease);
            Task ready = RequirePreceding(preceding);
            GpuResourceScope scope = Manager.CreateScope();
            try
            {
                GpuTextureRef native = scope.ImportTexture(texture, description, lease);
                if (preceding.IsValid) { Manager.AcquireUse(native).Retire(preceding); }
                var dimension = description.Dimension switch
                { NativeGpuTextureDimension.OneD => GpuGraphTextureDimension.OneD, NativeGpuTextureDimension.ThreeD => GpuGraphTextureDimension.ThreeD, _ => GpuGraphTextureDimension.TwoD };
                var common = new GpuGraphTextureDescription(description.Width, description.Height, description.Format,
                    DepthOrArrayLayers: description.Dimension == NativeGpuTextureDimension.ThreeD ? description.Depth : description.LayerCount,
                    MipLevelCount: description.MipCount, SampleCount: description.SampleCount, Dimension: dimension);
                GpuGraphTextureRef reference = Wrap(native, common); readiness.Add(reference, ready);
                return new(reference, Own(scope));
            }
            catch { scope.Dispose(); throw; }
        }
    }
    private Task RequirePreceding(GpuSubmissionToken token)
    {
        if (!token.IsValid) { return Task.CompletedTask; }
        if (!Manager.OwnsSubmission(token)) { throw new ArgumentException("The predecessor must be accepted by this runtime's main queue.", nameof(token)); }
        Task ready = token.WaitAsync();
        if (ready.IsCompleted) { ready.GetAwaiter().GetResult(); }
        return ready;
    }
    internal Task GetReadiness(GpuGraphResourceRef reference) => readiness.TryGetValue(reference, out Task? result) ? result : Task.CompletedTask;
}
