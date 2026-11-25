namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class MeshCoverageServiceRequest2D(MeshCoverageOptions2D options)
    : PipelineServiceRequest2D<MeshCoverageService2D, MeshCoverageOptions2D>("mesh-coverage", options, 1)
{
    protected override MeshCoverageService2D CreateService(PipelineBuildContext2D context) => new(context.Device, Options);
}
