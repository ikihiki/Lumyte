using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;

internal sealed class GraphicsDeclarationValueVisitor(GraphicsPlanBuilder plan) : IPipelineBundleVisitor
{
    private int _depth;
    private int _count;

    internal List<PlanResource> Resources { get; } = [];

    /// <inheritdoc/>
    public void Child<T>(T value)
    {
        if (++_count > 64 || _depth >= 16)
        {
            throw new InvalidOperationException("Graphics declaration bundle exceeds its finite traversal reservation.");
        }

        if (value is IPipelineBundle bundle)
        {
            _depth++;
            try
            {
                bundle.VisitChildren(this);
            }
            finally
            {
                _depth--;
            }
        }
        else if (value is IPlanningDeviceValue device)
        {
            if (value is not INonCpuReadablePlacement { Extent: not null })
            {
                throw new InvalidOperationException("Uninitialized graphics device declaration.");
            }

            PlanResource resource = device.Resource;
            plan.Validate(resource, resource.Range);
            Resources.Add(resource);
        }
        else if (value is INonCpuReadablePlacement)
        {
            throw new ArgumentException("The opaque declaration is not owned by this graphics backend.");
        }
    }
}
