using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class CoverageMembershipService2D(IGraphicDevice device) : PipelineSharedService2D
{
    internal DrawRecordResources2D Resources { get; } = new(device, 0);

    public override void Dispose() => Resources.Dispose();
}
