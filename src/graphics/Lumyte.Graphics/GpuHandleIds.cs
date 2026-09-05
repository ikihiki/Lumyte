namespace Lumyte.Graphics;

/// <summary>Issues opaque IDs unique across backend instances and resource kinds in this process.</summary>
public static class GpuHandleIds
{
    private static long lastId;

    public static ulong Allocate()
    {
        long value = Interlocked.Increment(ref lastId);
        if (value <= 0) { throw new InvalidOperationException("GPU handle ID space is exhausted."); }
        return (ulong)value;
    }
}
