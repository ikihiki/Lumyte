namespace Lumyte.Graphics.RenderGraph;

/// <summary>A backend presentation image acquired for one frame.</summary>
public readonly record struct GpuPresentationTarget(
    GpuTextureView View,
    GpuTextureDescription Description)
{
    public GpuPresentationTarget Validate()
    {
        Description.Validate();
        if (View.Id.IsNull || View.Texture.IsNull || View.Description.Format != Description.Format)
        {
            throw new ArgumentException("Presentation view and texture description do not match.", nameof(View));
        }
        if ((Description.Usage & GpuTextureUsage.ColorAttachment) == 0)
        {
            throw new ArgumentException("Presentation targets require ColorAttachment usage.", nameof(Description));
        }
        return this;
    }
}

/// <summary>Adapts a window system's acquire and present operations to a backend-independent frame.</summary>
public interface IGpuPresentationAdapter
{
    GpuPresentationTarget AcquireNextTarget();
    void Present(GpuPresentationTarget target, GpuSubmissionToken completion);
    void Discard(GpuPresentationTarget target) { }
}
