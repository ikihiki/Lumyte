namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class CoverageMembershipServiceRequest2D() : PipelineServiceRequest2D<CoverageMembershipService2D, int>("coverage-membership", 1, 1)
{
    protected override CoverageMembershipService2D CreateService(PipelineBuildContext2D context) => new(context.Device);
}
