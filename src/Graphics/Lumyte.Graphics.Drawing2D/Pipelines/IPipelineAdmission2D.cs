namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Optional pure admission implemented by a runtime processor, never its passive declaration.</summary>
/// <typeparam name="TCommand">The exact command kind.</typeparam>
/// <remarks>Legacy processors without this interface declare full batch support. Checks must not allocate GPU resources, write, record or submit. Exceptions propagate without retrying another candidate.</remarks>
public interface IPipelineAdmission2D<TCommand>
    where TCommand : DrawingCommand2D
{
    /// <summary>Checks the whole contiguous compatible batch against frozen configuration and input state.</summary>
    /// <param name="context">Borrowed preparation environment; do not retain it.</param>
    /// <param name="commands">The complete batch, including clip, paint and transform state.</param>
    /// <returns>Whether this algorithm can prepare the complete batch.</returns>
    bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<TCommand> commands);
}
