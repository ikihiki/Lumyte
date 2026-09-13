using Lumyte.Graphics.RenderGraph.Legacy;

namespace Lumyte.Graphics.Library;

public static class PresentationTargetExtensions
{
    public static DrawRenderTarget AsDrawTarget(
        this GpuPresentationTarget target,
        GpuAttachmentLoadOperation loadOperation = GpuAttachmentLoadOperation.Load,
        GpuClearColor clearColor = default)
        => new(target.View, target.Description, loadOperation, ClearColor: clearColor);
}
