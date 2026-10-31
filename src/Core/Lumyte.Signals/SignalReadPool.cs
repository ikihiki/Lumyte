using Microsoft.Extensions.ObjectPool;

namespace Lumyte.Signals;

internal static class SignalReadPool
{
    private static readonly ObjectPool<Dictionary<SignalNode, ulong>> _pool =
        new DefaultObjectPool<Dictionary<SignalNode, ulong>>(new ReadPolicy(), 64);

    internal static Dictionary<SignalNode, ulong> Rent() => _pool.Get();

    internal static void Return(Dictionary<SignalNode, ulong> reads) => _pool.Return(reads);

    private sealed class ReadPolicy : PooledObjectPolicy<Dictionary<SignalNode, ulong>>
    {
        public override Dictionary<SignalNode, ulong> Create() => [];

        public override bool Return(Dictionary<SignalNode, ulong> reads)
        {
            reads.Clear();
            return reads.EnsureCapacity(0) <= 1024;
        }
    }
}
