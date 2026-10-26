namespace Lumyte.Memory;

internal sealed class MemoryChunk<T>(MemoryPoolState pool, T[] array, ulong chargedBytes)
    where T : unmanaged
{
    private T[]? _array = array;
    private int _cursor;
    private int _references;

    internal MemoryPoolState Pool => pool;

    internal bool IsRetired => Volatile.Read(ref _references) < 0;

    internal bool TryRent(int length, out int offset)
    {
        offset = _cursor;
        T[]? storage = Volatile.Read(ref _array);
        if (storage is null || length > storage.Length - _cursor)
        {
            return false;
        }

        // Only the owning thread advances the cursor; returners only change the reference count.
        while (true)
        {
            int previous = Volatile.Read(ref _references);
            if (previous < 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _references, checked(previous + 1), previous) == previous)
            {
                _cursor += length;
                return true;
            }
        }
    }

    internal Memory<T> GetMemory(int offset, int length) => _array!.AsMemory(offset, length);

    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0 && Interlocked.CompareExchange(ref _references, -1, 0) == 0)
        {
            // Atomically retired chunks cannot accept a new lease from the owner thread.
            Volatile.Write(ref _array, null);
            pool.ReleaseCharge(new(chargedBytes, MemoryAccuracy.Exact));
        }
    }
}
