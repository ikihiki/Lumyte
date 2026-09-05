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

        OwnedBuffer result = Create(backend, checked((ulong)bytes.Length));
        try
        {
            result.Write(0, bytes);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public static OwnedBuffer Create(IGpuBackend backend, ulong size)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (size == 0 || (size & 3) != 0)
        {
            throw new ArgumentException("GPU buffer size must be non-zero and four-byte aligned.", nameof(size));
        }

        bool placed = (backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) != 0;
        var description = new GpuBufferDescription(
            size,
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
            return new(backend, buffer, description, allocation);
        }
        catch
        {
            if (!buffer.IsNull) { backend.DestroyBuffer(buffer); }
            if (!allocation.MemoryAddress.IsNull) { backend.FreeMemory(allocation); }
            throw;
        }
    }

    public static OwnedBuffer CreateStorage(IGpuBackend backend, ulong size)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (size == 0 || (size & 3) != 0)
        {
            throw new ArgumentException("GPU buffer size must be non-zero and four-byte aligned.", nameof(size));
        }

        var description = new GpuBufferDescription(
            size,
            GpuBufferUsage.ShaderData | GpuBufferUsage.Storage);
        bool placed = (backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) != 0;
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
                    GpuMemoryKind.DeviceLocal,
                    requirements.Compatibility);
                buffer = backend.CreatePlacedBuffer(description, allocation);
            }
            else
            {
                buffer = backend.CreateBuffer(description);
            }
            return new(backend, buffer, description, allocation);
        }
        catch
        {
            if (!buffer.IsNull) { backend.DestroyBuffer(buffer); }
            if (!allocation.MemoryAddress.IsNull) { backend.FreeMemory(allocation); }
            throw;
        }
    }

    public void Write(ulong destinationOffset, ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes.IsEmpty || (bytes.Length & 3) != 0 || (destinationOffset & 3) != 0
            || checked(destinationOffset + (ulong)bytes.Length) > Description.Size)
        {
            throw new ArgumentException("GPU buffer writes must be aligned and fit the buffer.", nameof(bytes));
        }
        backend.WriteBuffer(Buffer, destinationOffset, bytes);
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
