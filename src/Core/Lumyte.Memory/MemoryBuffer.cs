using System.Runtime.CompilerServices;

namespace Lumyte.Memory;

internal sealed class MemoryBuffer<T>(MemoryScope scope, MemoryChunk<T> chunk, int offset, int length) : IMemoryBuffer<T>, IMemoryAllocation
    where T : unmanaged
{
    private MemoryChunk<T>? _chunk = chunk;

    public int Capacity => length;

    public ulong ChargedBytes => checked((ulong)length * (uint)Unsafe.SizeOf<T>());

    public Memory<T> Memory => (Volatile.Read(ref _chunk) ?? throw new ObjectDisposedException(GetType().Name)).GetMemory(offset, length);

    public void Dispose() => scope.DisposeAllocation(this);

    public void Release()
    {
        MemoryChunk<T>? returned = Interlocked.Exchange(ref _chunk, null);
        if (returned is null)
        {
            return;
        }

        returned.Release();
        scope.Forget(this);
    }
}
