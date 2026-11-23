namespace Lumyte.Pipelines;

internal sealed class RunMemoryBudget(long limit)
{
    private readonly object _gate = new();
    private long _reserved;

    internal void Reserve(long bytes)
    {
        lock (_gate)
        {
            long next = checked(_reserved + bytes);
            if (bytes < 0 || next > limit)
            {
                throw new InvalidOperationException("Actual pipeline host requirements exceed the finite run budget before stage invocation.");
            }

            _reserved = next;
        }
    }
}
