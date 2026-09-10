namespace Lumyte.Graphics.Native;

/// <summary>A caller-owned completion timeline, independent of the command recording's lifetime.</summary>
public abstract class NativeGpuSemaphore : IDisposable
{
    protected NativeGpuSemaphore() { }

    /// <summary>Destroys the timeline after the caller has resolved all related submissions and waits.</summary>
    public abstract void Dispose();
}
