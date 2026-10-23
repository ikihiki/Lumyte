namespace Lumyte.Execution;

internal sealed class RangeClaim(int length, int batchSize)
{
    private long _next;

    internal bool HasMore => Volatile.Read(ref _next) < length;

    internal bool TryClaim(out int first, out int end)
    {
        long claimed = Interlocked.Add(ref _next, batchSize) - batchSize;
        first = (int)Math.Min(claimed, length);
        end = (int)Math.Min(claimed + batchSize, length);
        return first < end;
    }
}
