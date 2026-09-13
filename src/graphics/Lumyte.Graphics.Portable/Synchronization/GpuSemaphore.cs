namespace Lumyte.Graphics.Portable;

/// <summary>A caller-owned CPU-observed timeline belonging to one Portable queue.</summary>
/// <remarks>The caller ends all associated submissions and waits before disposing the timeline or its backend.</remarks>
public abstract class GpuSemaphore : IDisposable
{
    protected GpuSemaphore() { }
    public abstract void Dispose();
}
