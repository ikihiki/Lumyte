namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive algorithm configuration distinct from its compiled processor.</summary>
public abstract class PipelineDeclaration2D
{
    /// <summary>Gets this passive identity.</summary>
    public PipelineDeclaration2D Declaration => this;

    /// <summary>Gets an optional diagnostic label; it never controls identity or sharing.</summary>
    public virtual string? Name => null;

    /// <summary>Gets declared processor metadata without creating an instance.</summary>
    public abstract Type ProcessorType { get; }

    internal virtual bool OwnsProcessor => true;

    internal static PipelineOccurrence2D[] CaptureGraph(IEnumerable<PipelineDeclaration2D> roots, string kind)
    {
        var active = new HashSet<PipelineDeclaration2D>(ReferenceEqualityComparer.Instance);
        PipelineOccurrence2D Visit(PipelineDeclaration2D declaration, string path)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (active.Contains(declaration))
            {
                throw new ArgumentException("Recursive passive child topology is not supported.");
            }

            Type declarationType = declaration.GetType();
            if (typeof(IPipelineDeclaration2D<FillRectCommand2D>).IsAssignableFrom(declarationType) && !typeof(IFillRectRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<FillRoundedRectCommand2D>).IsAssignableFrom(declarationType) && !typeof(IFillRoundedRectRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<FillCircleCommand2D>).IsAssignableFrom(declarationType) && !typeof(IFillCircleRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawLineCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawLineRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<FillPathCommand2D>).IsAssignableFrom(declarationType) && !typeof(IFillPathRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<StrokePathCommand2D>).IsAssignableFrom(declarationType) && !typeof(IStrokePathRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawImageCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawImageRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawCoverageMaskCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawCoverageMaskRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawDistanceFieldCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawDistanceFieldRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawMeshCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawMeshRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            if (typeof(IPipelineDeclaration2D<DrawPaintGraphCommand2D>).IsAssignableFrom(declarationType) && !typeof(IDrawPaintGraphRenderer2D).IsAssignableFrom(declaration.ProcessorType))
            {
                throw new ArgumentException("The nested declaration processor does not implement its typed capability.");
            }

            active.Add(declaration);
            PipelineOccurrence2D[] children = declaration.GetChildDeclarations().Select((child, index) => Visit(child, $"{path}/{index}")).ToArray();
            active.Remove(declaration);
            return new(declaration, path, children);
        }

        return roots.Select((root, index) => Visit(root, $"{kind}/{index}")).ToArray();
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
