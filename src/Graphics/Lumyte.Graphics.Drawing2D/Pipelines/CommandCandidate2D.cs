namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>A pure batch admission check paired with an original typed backend.</summary>
/// <typeparam name="TCommand">The exact command kind.</typeparam>
/// <typeparam name="TBackend">The original renderer interface.</typeparam>
/// <param name="Supports">Pure admission before any selected backend preparation or GPU allocation.</param>
/// <param name="Backend">The borrowed backend; resource failures never cause fallback.</param>
public sealed record CommandCandidate2D<TCommand, TBackend>(Func<RenderPreparationContext2D, ReadOnlyMemory<TCommand>, bool> Supports, TBackend Backend)
    where TCommand : DrawingCommand2D
    where TBackend : class;
