namespace Lumyte.Graphics;

/// <summary>
/// Owns a managed wrapper for a backend resource.
/// </summary>
public abstract class GpuResource : IDisposable
{
    internal GpuResource(IGraphicsDriver driver, object handle)
    {
        (Driver, Handle) = (driver, handle);
    }

    internal IGraphicsDriver Driver { get; }

    internal object Handle { get; }

    /// <summary>
    /// Releases an idle resource; recorded or in-flight leases prevent disposal. Repeated disposal is harmless.
    /// </summary>
    public void Dispose() => Driver.DisposeHandle(Handle);
}
