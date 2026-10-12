using System.Buffers;

namespace Lumyte.Memory;

/// <summary>Owns a typed pooled CPU buffer and its memory charge.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public interface IMemoryBuffer<T> : IMemoryOwner<T>
    where T : unmanaged
{
    /// <summary>Gets the rented element capacity.</summary>
    int Capacity { get; }

    /// <summary>Gets the byte count charged for the capacity.</summary>
    ulong ChargedBytes { get; }
}
