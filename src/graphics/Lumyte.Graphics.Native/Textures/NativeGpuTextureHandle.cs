namespace Lumyte.Graphics.Native;

/// <summary>An opaque, separately owned texture placed in a caller-owned heap.</summary>
public abstract class NativeGpuTextureHandle
{
    /// <summary>Allows a backend to retain its texture state in a private derived type.</summary>
    protected NativeGpuTextureHandle() { }
}
