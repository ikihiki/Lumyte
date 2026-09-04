namespace Lumyte.Graphics.TwoD;

public readonly record struct RenderTarget(
    GpuTextureHandle Texture,
    GpuTextureDescription Description,
    GpuAttachmentLoadOperation LoadOperation = GpuAttachmentLoadOperation.Load,
    GpuAttachmentStoreOperation StoreOperation = GpuAttachmentStoreOperation.Store,
    GpuClearColor ClearColor = default)
{
    public RenderTarget Validate()
    {
        Description.Validate();
        if (Texture.IsNull)
        {
            throw new ArgumentException("Target texture cannot be null.", nameof(Texture));
        }
        if ((Description.Usage & GpuTextureUsage.ColorAttachment) == 0)
        {
            throw new ArgumentException("Target description requires color-attachment usage.", nameof(Description));
        }
        if (!Enum.IsDefined(LoadOperation) || !Enum.IsDefined(StoreOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(LoadOperation));
        }
        return this;
    }
}
