namespace Lumyte.Graphics.TwoD;

internal sealed class OwnedBuffer : IDisposable
{
    private readonly IGpuBackend backend;
    private GpuMemoryAllocation allocation;
    private bool disposed;

    private OwnedBuffer(
        IGpuBackend backend,
        GpuBufferHandle buffer,
        GpuBufferDescription description,
        GpuMemoryAllocation allocation)
    {
        this.backend = backend;
        Buffer = buffer;
        Description = description;
        this.allocation = allocation;
    }

    public GpuBufferHandle Buffer { get; }
    public GpuBufferDescription Description { get; }

    public static OwnedBuffer Create(IGpuBackend backend, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (bytes.IsEmpty || (bytes.Length & 3) != 0)
        {
            throw new ArgumentException("GPU buffer data must be non-empty and four-byte aligned.", nameof(bytes));
        }

        bool placed = (backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) != 0;
        var description = new GpuBufferDescription(
            checked((ulong)bytes.Length),
            GpuBufferUsage.ShaderData
                | (placed ? GpuBufferUsage.CopySource : GpuBufferUsage.CopyDestination));
        GpuMemoryAllocation allocation = default;
        GpuBufferHandle buffer = default;
        try
        {
            if (placed)
            {
                GpuBufferMemoryRequirements requirements = backend
                    .GetBufferMemoryRequirements(description)
                    .Validate();
                allocation = backend.AllocateMemory(
                    requirements.Size,
                    requirements.Alignment,
                    GpuMemoryKind.HostMapped,
                    requirements.Compatibility);
                buffer = backend.CreatePlacedBuffer(description, allocation);
            }
            else
            {
                buffer = backend.CreateBuffer(description);
            }

            backend.WriteBuffer(buffer, bytes);
            return new(backend, buffer, description, allocation);
        }
        catch
        {
            if (!buffer.IsNull) { backend.DestroyBuffer(buffer); }
            if (!allocation.MemoryAddress.IsNull) { backend.FreeMemory(allocation); }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        backend.DestroyBuffer(Buffer);
        if (!allocation.MemoryAddress.IsNull)
        {
            backend.FreeMemory(allocation);
            allocation = default;
        }
        disposed = true;
    }
}
