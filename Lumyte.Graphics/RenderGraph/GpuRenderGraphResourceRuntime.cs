namespace Lumyte.Graphics.RenderGraph;

internal sealed class GpuRenderGraphResourceRuntime
{
    private GpuMemoryAllocation? allocation;
    private bool ownsResource;

    private GpuRenderGraphResourceRuntime(GpuRenderGraphResourceInfo info)
    {
        Info = info;
        Texture = info.Texture;
        Buffer = info.Buffer;
    }

    public GpuRenderGraphResourceInfo Info { get; }
    public GpuTextureHandle Texture { get; private set; }
    public GpuBufferHandle Buffer { get; private set; }
    public GpuTextureView? View { get; set; }
    public Dictionary<GpuBufferViewDescription, GpuBufferView> BufferViews { get; } = [];

    public static GpuRenderGraphResourceRuntime Import(GpuRenderGraphResourceInfo info)
        => new(info);

    public static GpuRenderGraphResourceRuntime Create(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info)
    {
        var result = new GpuRenderGraphResourceRuntime(info)
        {
            ownsResource = true,
        };
        try
        {
            if (info.Kind == GpuRenderGraphResourceKind.Texture)
            {
                result.CreateTexture(backend);
            }
            else
            {
                result.CreateBuffer(backend);
            }
            return result;
        }
        catch
        {
            result.Dispose(backend);
            throw;
        }
    }

    public static GpuRenderGraphResourceRuntime Create(
        IGpuBackend backend,
        GpuRenderGraphResourceInfo info,
        GpuMemoryAllocation allocation)
    {
        var result = new GpuRenderGraphResourceRuntime(info)
        {
            ownsResource = true,
        };
        try
        {
            if (info.Kind == GpuRenderGraphResourceKind.Texture)
            {
                GpuTextureDescription description = info.TextureDescription
                    ?? throw new InvalidOperationException("Transient texture has no description.");
                result.Texture = backend.CreatePlacedTexture(description, allocation);
            }
            else
            {
                GpuBufferDescription description = info.BufferDescription
                    ?? throw new InvalidOperationException("Transient buffer has no description.");
                result.Buffer = backend.CreatePlacedBuffer(description, allocation);
            }
            return result;
        }
        catch
        {
            result.Dispose(backend);
            throw;
        }
    }

    public void Dispose(IGpuBackend backend)
    {
        if (!ownsResource) { return; }
        if (!Texture.IsNull)
        {
            backend.DestroyTexture(Texture);
            Texture = default;
        }
        if (!Buffer.IsNull)
        {
            backend.DestroyBuffer(Buffer);
            Buffer = default;
        }
        if (allocation is { } memory)
        {
            backend.FreeMemory(memory);
            allocation = null;
        }
        ownsResource = false;
    }

    private void CreateTexture(IGpuBackend backend)
    {
        GpuTextureDescription description = Info.TextureDescription
            ?? throw new InvalidOperationException("Transient texture has no description.");
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            Texture = backend.CreateTexture(description);
            return;
        }
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new NotSupportedException("The backend cannot create render-graph textures.");
        }

        GpuTextureMemoryRequirements requirements =
            backend.GetTextureMemoryRequirements(description);
        allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            Info.MemoryKind);
        Texture = backend.CreatePlacedTexture(description, allocation.Value);
    }

    private void CreateBuffer(IGpuBackend backend)
    {
        GpuBufferDescription description = Info.BufferDescription
            ?? throw new InvalidOperationException("Transient buffer has no description.");
        if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
        {
            Buffer = backend.CreateBuffer(description);
            return;
        }
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new NotSupportedException("The backend cannot create render-graph buffers.");
        }

        GpuBufferMemoryRequirements requirements =
            backend.GetBufferMemoryRequirements(description);
        allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            Info.MemoryKind);
        Buffer = backend.CreatePlacedBuffer(description, allocation.Value);
    }
}
