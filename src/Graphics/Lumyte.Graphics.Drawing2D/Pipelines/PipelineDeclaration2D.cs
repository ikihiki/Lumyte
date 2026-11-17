namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive algorithm configuration distinct from its compiled processor.</summary>
public abstract class PipelineDeclaration2D
{
    /// <summary>Gets this passive identity.</summary>
    public PipelineDeclaration2D Declaration => this;

    /// <summary>Gets an optional graph-local diagnostic name.</summary>
    public virtual string? Name => null;

    /// <summary>Gets declared processor metadata without creating an instance.</summary>
    public abstract Type ProcessorType { get; }

    internal virtual IReadOnlyList<Type> ChoiceProcessors => Array.Empty<Type>();

    internal virtual bool OwnsProcessor => true;

    internal static IReadOnlySet<PipelineDeclaration2D> ValidateGraph(IEnumerable<PipelineDeclaration2D> roots)
    {
        var visited = new HashSet<PipelineDeclaration2D>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<PipelineDeclaration2D>(ReferenceEqualityComparer.Instance);
        var names = new Dictionary<string, PipelineDeclaration2D>(StringComparer.Ordinal);
        void Visit(PipelineDeclaration2D declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (active.Contains(declaration))
            {
                throw new ArgumentException("Recursive passive child topology is not supported.");
            }

            if (!visited.Add(declaration))
            {
                return;
            }

            if (declaration.Name is string name)
            {
                if (string.IsNullOrWhiteSpace(name) || (names.TryGetValue(name, out PipelineDeclaration2D? previous) && !ReferenceEquals(previous, declaration)))
                {
                    throw new ArgumentException("Conflicting graph-local declaration identities.");
                }

                names[name] = declaration;
            }

            active.Add(declaration);
            foreach (PipelineDeclaration2D child in declaration.GetChildDeclarations())
            {
                Visit(child);
            }

            active.Remove(declaration);
        }

        foreach (PipelineDeclaration2D root in roots)
        {
            Visit(root);
        }

        return visited;
    }

    internal object CreateInstance(PipelineBuildContext2D context) => CreateElement(context) ?? throw new InvalidOperationException("CreateElement returned null.");

    internal void ValidateInstance(object result)
    {
        if (ReferenceEquals(result, this) || !ProcessorType.IsInstanceOfType(result))
        {
            throw new InvalidOperationException("CreateElement must return its distinct declared processor type.");
        }

        if (OwnsProcessor && result is not PipelineElement2D && !(this is DrawingCompose.Definitions.StandardFillRect || this is DrawingCompose.Definitions.StandardFillRoundedRect || this is DrawingCompose.Definitions.StandardFillCircle || this is DrawingCompose.Definitions.StandardDrawLine || this is DrawingCompose.Definitions.StandardFillPath || this is DrawingCompose.Definitions.StandardStrokePath || this is DrawingCompose.Definitions.StandardDrawImage || this is DrawingCompose.Definitions.StandardDrawCoverageMask || this is DrawingCompose.Definitions.StandardDrawDistanceField || this is DrawingCompose.Definitions.StandardDrawMesh || this is DrawingCompose.Definitions.StandardDrawPaintGraph))
        {
            throw new InvalidOperationException("Owned custom processors must derive from PipelineElement2D; use the explicit borrowed adapter for existing caller-owned callbacks.");
        }
    }

    /// <summary>Creates a fresh runtime processor only during compilation.</summary>
    /// <returns>A distinct processing object implementing the declared command interfaces.</returns>
    /// <param name="context">The validated construction environment.</param>
    protected abstract object CreateElement(PipelineBuildContext2D context);

    /// <summary>Describes passive children before any runtime processing instances are created.</summary>
    /// <returns>Explicit child identities; shared references may appear more than once.</returns>
    protected virtual IEnumerable<PipelineDeclaration2D> GetChildDeclarations() => Array.Empty<PipelineDeclaration2D>();
}
