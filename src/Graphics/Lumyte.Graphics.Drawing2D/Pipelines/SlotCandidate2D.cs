namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed record SlotCandidate2D<TCommand, TBackend>(TBackend Backend)
    where TCommand : DrawingCommand2D
    where TBackend : class;
