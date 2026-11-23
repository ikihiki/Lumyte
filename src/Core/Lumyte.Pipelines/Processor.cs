namespace Lumyte.Pipelines;

internal abstract class Processor : IDisposable
{
    public abstract void Dispose();

    internal abstract ValueTask<object> PrepareAsync(StagePreparationContext context, object[] inputs, CancellationToken cancellationToken);
}
