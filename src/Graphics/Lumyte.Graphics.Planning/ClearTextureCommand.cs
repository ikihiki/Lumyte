using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ClearTextureCommand(PlanTexture target, ClearColor color) : PlanCommand
{
    private RenderPassDesc? _description;

    internal override IReadOnlyList<PlanResource> PhysicalResources => [target];

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables) => _description = new()
    {
        ColorAttachments = [new()
        {
            View = resources.Texture(target),
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = color,
        }

        ],
    };

    internal override void Record(IGraphicsCommandBuffer commands)
    {
        IRenderEncoder encoder = commands.BeginRenderPass(_description!);
        encoder.End();
    }
}
