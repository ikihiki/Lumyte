namespace Lumyte.Graphics.Text;

/// <summary>Owns one lazily uploaded color-glyph texture.</summary>
internal sealed class ColorBitmapTexture : IDisposable
{
    private readonly IGpuBackend backend;
    private GpuMemoryAllocation allocation;
    private bool disposed;

    private ColorBitmapTexture(
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

    internal GpuTextureHandle Texture { get; }
    internal GpuTextureDescription Description { get; }

    internal static ColorBitmapTexture Create(
        IGpuBackend backend,
        uint width,
        uint height,
        ReadOnlySpan<byte> pixels)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var description = new GpuTextureDescription(
            width,
            height,
            GpuFormat.Rgba8UnormSrgb,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        description.Validate();
        ulong byteLength = checked((ulong)width * height * 4);
        if ((ulong)pixels.Length != byteLength)
        {
            throw new ArgumentException("Color glyph pixels must contain one RGBA8 value per pixel.", nameof(pixels));
        }

        GpuMemoryAllocation allocation = default;
        GpuTextureHandle texture = default;
        try
        {
            if ((backend.Capabilities & GpuBackendCapabilities.DeviceOwnedResources) != 0)
            {
                texture = backend.CreateTexture(description);
                backend.WriteTexture(texture, pixels, new(width, height, 4, checked((ulong)width * 4)));
            }
            else
            {
                GpuTextureMemoryRequirements requirements = backend
                    .GetTextureMemoryRequirements(description)
                    .Validate();
                allocation = backend.AllocateMemory(
                    requirements.Size,
                    requirements.Alignment,
                    GpuMemoryKind.DeviceLocal,
                    requirements.Compatibility);
                texture = backend.CreatePlacedTexture(description, allocation);
                Upload(backend, texture, width, height, pixels);
            }
            return new(backend, texture, description, allocation);
        }
        catch
        {
            if (!texture.IsNull)
            {
                backend.DestroyTexture(texture);
            }
            if (!allocation.MemoryAddress.IsNull)
            {
                backend.FreeMemory(allocation);
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        backend.DestroyTexture(Texture);
        if (!allocation.MemoryAddress.IsNull)
        {
            backend.FreeMemory(allocation);
            allocation = default;
        }
        disposed = true;
    }

    private static void Upload(
        IGpuBackend backend,
        GpuTextureHandle texture,
        uint width,
        uint height,
        ReadOnlySpan<byte> pixels)
    {
        var bufferDescription = new GpuBufferDescription(
            checked((ulong)pixels.Length),
            GpuBufferUsage.CopySource);
        GpuBufferMemoryRequirements requirements = backend
            .GetBufferMemoryRequirements(bufferDescription)
            .Validate();
        GpuMemoryAllocation allocation = backend.AllocateMemory(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.HostMapped,
            requirements.Compatibility);
        GpuBufferHandle upload = default;
        try
        {
            upload = backend.CreatePlacedBuffer(bufferDescription, allocation);
            backend.WriteBuffer(upload, pixels);
            var footprint = new GpuTextureCopyFootprint(
                width,
                height,
                4,
                checked((ulong)width * 4));
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
                .CopyMemoryToTexture(
                    backend.GetBufferMemoryAddress(upload, 0, checked((ulong)pixels.Length)),
                    texture,
                    footprint)
                .Barrier(GpuStage.Copy, GpuStage.PixelShader);
            using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
            backend.MainQueue.Submit([commands], completion, 1);
            backend.MainQueue.Wait(completion, 1);
        }
        finally
        {
            if (!upload.IsNull)
            {
                backend.DestroyBuffer(upload);
            }
            backend.FreeMemory(allocation);
        }
    }
}
