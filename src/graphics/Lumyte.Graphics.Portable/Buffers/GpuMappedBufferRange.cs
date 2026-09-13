namespace Lumyte.Graphics.Portable;

/// <summary>A caller-owned mapping lease. Disposing it unmaps the buffer, without destroying the buffer.</summary>
/// <remarks>
/// Memory has a logical lifetime ending at Dispose. A backend rejects subsequent memory/span acquisition after unmap.
/// Already acquired spans and pointers cannot be revoked: callers must not retain or use them after Dispose,
/// or race any mapped-memory access with unmapping. Memory length must be representable by System.Memory.
/// </remarks>
public abstract class GpuMappedBufferRange : IDisposable
{
    protected GpuMappedBufferRange() { }

    /// <summary>Writable mapped memory. Access on a read mapping throws InvalidOperationException.</summary>
    public abstract Memory<byte> Memory { get; }

    /// <summary>A read-only view available for both read and write mappings.</summary>
    public abstract ReadOnlyMemory<byte> ReadOnlyMemory { get; }

    /// <summary>Ends the mapping once. All uses of acquired memory must already have ended.</summary>
    public abstract void Dispose();
}
