namespace Lumyte.Graphics.Native;

/// <summary>An opaque allocation requirement from one backend and memory kind.</summary>
public abstract class NativeGpuMemoryCompatibility
{
    /// <summary>Allows a backend to keep its allocation requirements in a private derived type.</summary>
    protected NativeGpuMemoryCompatibility() { }
}
