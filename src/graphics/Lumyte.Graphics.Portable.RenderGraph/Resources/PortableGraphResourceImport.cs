using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Portable.RenderGraph;

/// <summary>Explicit ownership of a raw object exposed as a common graph reference.</summary>
public sealed class PortableGraphResourceImport<TReference> : IDisposable where TReference : GpuGraphResourceRef
{
    private Action<IDisposable>? release;
    internal PortableGraphResourceImport(TReference reference, Action<IDisposable> release)
    { Reference = reference; this.release = release; }
    public TReference Reference { get; }
    /// <summary>Ends host ownership. Already acquired graph/preceding submission uses remain retained.</summary>
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke(this);
}
