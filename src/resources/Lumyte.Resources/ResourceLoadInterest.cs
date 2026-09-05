namespace Lumyte.Resources;

internal sealed class ResourceLoadInterest
{
    private readonly CancellationTokenSource cancellation = new();
    private int waiterCount;

    internal CancellationToken CancellationToken => cancellation.Token;

    internal void AddWaiter() => Interlocked.Increment(ref waiterCount);

    internal bool RemoveWaiter()
    {
        int remaining = Interlocked.Decrement(ref waiterCount);
        if (remaining < 0)
        {
            throw new InvalidOperationException("A resource load waiter was removed too many times.");
        }

        if (remaining == 0)
        {
            cancellation.Cancel();
            return true;
        }

        return false;
    }
}
