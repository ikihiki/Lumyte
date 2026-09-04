namespace Lumyte.Graphics.TwoD;

public readonly record struct RenderTargetOptions(
    GpuAttachmentLoadOperation LoadOperation = GpuAttachmentLoadOperation.Load,
    GpuAttachmentStoreOperation StoreOperation = GpuAttachmentStoreOperation.Store,
    GpuClearColor ClearColor = default)
{
    public RenderTargetOptions Validate()
    {
        if (!Enum.IsDefined(LoadOperation) || !Enum.IsDefined(StoreOperation))
        {
            throw new ArgumentOutOfRangeException(nameof(LoadOperation));
        }
        return this;
    }
}
