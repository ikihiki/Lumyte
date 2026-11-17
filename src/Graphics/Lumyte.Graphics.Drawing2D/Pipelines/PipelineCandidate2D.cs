namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Passive kind-local admission and child algorithm configuration.</summary>
/// <typeparam name="TCommand">The exact logical command kind.</typeparam>
/// <param name="Supports">Pure admission before selected preparation or GPU work.</param>
/// <param name="Pipeline">The passive child declaration.</param>
public sealed record PipelineCandidate2D<TCommand>(
    Func<RenderPreparationContext2D, ReadOnlyMemory<TCommand>, bool> Supports,
    IPipelineDeclaration2D<TCommand> Pipeline)
    where TCommand : DrawingCommand2D;
