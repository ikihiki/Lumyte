namespace Lumyte.Graphics.Pipelines;

internal sealed class GraphicsScratchBudget(long limit)
{
    private readonly object _gate = new();
    private long _bytes;

    internal void Reserve(long bytes)
    {
        lock (_gate)
        {
            long next = checked(_bytes + bytes);
            if (bytes < 0 || next > limit)
            {
                throw new InvalidOperationException("Peak live graphics scratch admission exceeded before allocation.");
            }

            _bytes = next;
        }
    }

    internal void Release(long bytes)
    {
        lock (_gate)
        {
            _bytes = checked(_bytes - bytes);
        }
    }
}
