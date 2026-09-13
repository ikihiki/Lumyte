using Lumyte.Graphics.RenderGraph.Legacy;

namespace Lumyte.Graphics.TwoD;

public static class PresentationTargetExtensions
{
    public static RenderTarget AsTwoDTarget(
        this GpuPresentationTarget target,
        GpuAttachmentLoadOperation loadOperation = GpuAttachmentLoadOperation.Load,
        GpuClearColor clearColor = default)
        => new(target.View.Texture, target.Description, loadOperation, ClearColor: clearColor);
}
