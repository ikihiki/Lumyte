namespace Lumyte.Pipelines;

/// <summary>Explicit immutable recursive bundle shape, enumerating every contained payload without reflection.</summary>
/// <remarks>Visit children deterministically, exactly once, including default values. Omitted children violate the author contract. Core bounds traversal to 16 nesting levels and 64 values.</remarks>
public interface IPipelineBundle
{
    /// <summary>Visits every immediate typed child of this immutable shape.</summary>
    /// <param name="visitor">The bounded shape visitor.</param>
    void VisitChildren(IPipelineBundleVisitor visitor);
}
