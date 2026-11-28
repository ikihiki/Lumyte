namespace Lumyte.Pipelines;

/// <summary>A supported recursive immutable bundle. CPU access is denied if either child is opaque.</summary>
/// <typeparam name="TA">The first payload.</typeparam>
/// <typeparam name="TB">The second payload.</typeparam>
/// <param name="First">The first child.</param>
/// <param name="Second">The second child.</param>
public readonly record struct PipelineBundle<TA, TB>(TA First, TB Second) : IPipelineBundle
{
    /// <inheritdoc/>
    void IPipelineBundle.VisitChildren(IPipelineBundleVisitor visitor)
    {
        visitor.Child(First);
        visitor.Child(Second);
    }
}
