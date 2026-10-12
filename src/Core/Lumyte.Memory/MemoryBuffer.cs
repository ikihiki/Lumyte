using System.Buffers;

namespace Lumyte.Memory;

internal sealed class MemoryBuffer<T>(MemoryScope scope, MemoryCharge charge, T[] array, int length, ulong chargedBytes) : IMemoryBuffer<T>
    where T : unmanaged
{
    private T[]? _array = array;

    public int Capacity { get; } = array.Length;

    public ulong ChargedBytes { get; } = chargedBytes;

    public Memory<T> Memory => (_array ?? throw new ObjectDisposedException(GetType().Name)).AsMemory(0, length);

    public void Dispose()
    {
        T[]? returned = Interlocked.Exchange(ref _array, null);
        if (returned is null)
        {
            return;
        }

        ArrayPool<T>.Shared.Return(returned);
        charge.Dispose();
        scope.Forget(this);
    }
}
