namespace Lumyte.Graphics.TwoD;

internal sealed class OwnedTexture : IDisposable
{
    private readonly IGpuBackend backend;
    private GpuMemoryAllocation allocation;
    private bool disposed;

    private OwnedTexture(
        IGpuBackend backend,
        GpuTextureHandle texture,
        GpuTextureDescription description,
        GpuMemoryAllocation allocation)
    {
        this.backend = backend;
        Texture = texture;
        Description = description;
        this.allocation = allocation;
    }

    public GpuTextureHandle Texture { get; }
    public GpuTextureDescription Description { get; }

    public static OwnedTexture Create(IGpuBackend backend, GpuTextureDescription description)
    {
        ArgumentNullException.ThrowIfNull(backend);
        description.Validate();
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            return new(backend, backend.CreateTexture(description), description, default);
        }

        GpuTextureMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description).Validate();
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.DeviceLocal,
            requirements.Compatibility);
        try
        {
            return new(
                backend,
                backend.CreatePlacedTexture(description, allocation),
                description,
                allocation);
        }
        catch
        {
            backend.FreeMemory(allocation);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        backend.DestroyTexture(Texture);
        if (!allocation.MemoryAddress.IsNull)
        {
            backend.FreeMemory(allocation);
            allocation = default;
        }
        disposed = true;
    }
}
