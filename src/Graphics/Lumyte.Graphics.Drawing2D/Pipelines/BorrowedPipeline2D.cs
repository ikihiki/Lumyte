namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit compatibility adapter for a caller-owned legacy processor.</summary>
/// <typeparam name="TCommand">The logical command capability.</typeparam>
/// <remarks>Adapts an existing processor without ownership transfer.</remarks>
/// <param name="processor">The borrowed typed legacy processor.</param>
public sealed class BorrowedPipeline2D<TCommand>(object processor) : PipelineDeclaration2D, IPipelineDeclaration2D<TCommand>
    where TCommand : DrawingCommand2D
{
    private readonly object _processor = processor ?? throw new ArgumentNullException(nameof(processor));

    /// <inheritdoc/>
    public override Type ProcessorType => _processor.GetType();

    internal override bool OwnsProcessor => false;

    internal override IReadOnlyList<Type> ChoiceProcessors => (_processor as ICommandChoice2D)?.CandidateTypes ?? Array.Empty<Type>();

    /// <inheritdoc/>
    protected override object CreateElement(PipelineBuildContext2D context) => _processor;
}
