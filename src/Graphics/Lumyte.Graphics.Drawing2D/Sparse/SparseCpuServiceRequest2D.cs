using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SparseCpuServiceRequest2D(SparseCpuOptions2D options)
    : PipelineServiceRequest2D<SparseCpuService2D, SparseCpuOptions2D>("drawing2d.cpu-sparse", options, 1)
{
    protected override SparseCpuService2D CreateService(PipelineBuildContext2D context) => new(Options);
}
