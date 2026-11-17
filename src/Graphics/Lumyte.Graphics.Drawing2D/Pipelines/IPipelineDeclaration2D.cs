namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive capability marker without preparation, recording or lifetime operations.</summary>
/// <typeparam name="TCommand">The logical command capability.</typeparam>
public interface IPipelineDeclaration2D<TCommand>
    where TCommand : DrawingCommand2D
{
    /// <summary>Gets the graph-local passive identity.</summary>
    PipelineDeclaration2D Declaration { get; }
}
