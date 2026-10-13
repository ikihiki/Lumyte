using System.Runtime.CompilerServices;
using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Configures bounded streaming buffers.</summary>
public sealed record ResourceStreamOptions
{
    /// <summary>Gets the positive number of elements in each buffer block.</summary>
    public int BlockSize { get; init; } = 4096;

    /// <summary>Gets the positive maximum number of allocated buffer blocks.</summary>
    public int MaxBufferedBlocks { get; init; } = 2;

    /// <summary>Gets the memory pool charged for the session's buffer blocks.</summary>
    public MemoryPoolId BufferPool { get; init; } = new("cpu");

    internal void Validate<TElement>()
        where TElement : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BlockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBufferedBlocks);
        ArgumentException.ThrowIfNullOrEmpty(BufferPool.Value);
        _ = checked(BlockSize * MaxBufferedBlocks);
        _ = checked((ulong)BlockSize * (ulong)MaxBufferedBlocks * (ulong)Unsafe.SizeOf<TElement>());
    }
}
