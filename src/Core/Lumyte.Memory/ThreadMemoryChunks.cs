using System.Runtime.CompilerServices;

namespace Lumyte.Memory;

internal static class ThreadMemoryChunks<T>
    where T : unmanaged
{
    [ThreadStatic]
    private static List<WeakReference<MemoryChunk<T>>>? _chunks;

    internal static MemoryChunk<T> Rent(MemoryPoolState pool, int length, int chunkSizeBytes, out int offset)
    {
        List<WeakReference<MemoryChunk<T>>> chunks = _chunks ??= [];
        int currentIndex = -1;
        for (int index = chunks.Count - 1; index >= 0; index--)
        {
            if (!chunks[index].TryGetTarget(out MemoryChunk<T>? chunk) || chunk.IsRetired)
            {
                chunks.RemoveAt(index);
                continue;
            }

            if (!ReferenceEquals(chunk.Pool, pool))
            {
                continue;
            }

            if (chunk.TryRent(length, out offset))
            {
                return chunk;
            }

            currentIndex = index;
            break;
        }

        MemoryChunk<T> next = CreateChunk(pool, length, chunkSizeBytes);
        next.TryRent(length, out offset);
        try
        {
            if (currentIndex < 0)
            {
                chunks.Add(new(next));
            }
            else
            {
                chunks[currentIndex].SetTarget(next);
            }

            return next;
        }
        catch
        {
            next.Release();
            throw;
        }
    }

    private static MemoryChunk<T> CreateChunk(MemoryPoolState pool, int length, int chunkSizeBytes)
    {
        int capacity = Math.Max(length, chunkSizeBytes / Unsafe.SizeOf<T>());
        ulong bytes = checked((ulong)capacity * (uint)Unsafe.SizeOf<T>());
        try
        {
            pool.Reserve(bytes);
        }
        catch (MemoryBudgetExceededException) when (capacity != length)
        {
            capacity = length;
            bytes = checked((ulong)capacity * (uint)Unsafe.SizeOf<T>());
            pool.Reserve(bytes);
        }

        try
        {
            T[] array = GC.AllocateUninitializedArray<T>(capacity);
            var chunk = new MemoryChunk<T>(pool, array, bytes);
            pool.Commit(bytes, new(bytes, MemoryAccuracy.Exact), true);
            return chunk;
        }
        catch
        {
            pool.ReleaseReservation(bytes);
            throw;
        }
    }
}
