using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class CopyTextureCommand(PlanTexture source, PlanTextureRegion sourceRegion, PlanTexture destination, PlanTextureRegion destinationRegion) : PlanCommand
{
    private TextureCopyRegion? _src;
    private TextureCopyRegion? _dst;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables) => (_src, _dst) = (sourceRegion.Resolve(source, resources), destinationRegion.Resolve(destination, resources));

    internal override void Record(IGraphicsCommandBuffer commands) => commands.CopyTexture(_src!, _dst!);
}
