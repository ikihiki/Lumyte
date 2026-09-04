namespace Lumyte.Graphics.Library;

public readonly record struct DrawRenderTarget(
    GpuTextureView View,
    GpuTextureDescription Description,
    GpuAttachmentLoadOperation LoadOperation = GpuAttachmentLoadOperation.Load,
    GpuAttachmentStoreOperation StoreOperation = GpuAttachmentStoreOperation.Store,
    GpuClearColor ClearColor = default)
{
    public DrawRenderTarget Validate()
    {
        Description.Validate();
        if (View.Id.IsNull || View.Texture.IsNull || View.Description.Format != Description.Format)
        {
            throw new ArgumentException("Target view and description do not match.", nameof(View));
        }
        if ((Description.Usage & GpuTextureUsage.ColorAttachment) == 0)
        {
            throw new ArgumentException("Target description requires ColorAttachment usage.", nameof(Description));
        }
        if (!Enum.IsDefined(LoadOperation) || !Enum.IsDefined(StoreOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(LoadOperation));
        }
        return this;
    }
}
