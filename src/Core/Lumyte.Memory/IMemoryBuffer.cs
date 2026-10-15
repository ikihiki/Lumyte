using System.Buffers;

namespace Lumyte.Memory;

/// <summary>Owns a typed slice of a CPU chunk associated with the allocating thread.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public interface IMemoryBuffer<T> : IMemoryOwner<T>
    where T : unmanaged
{
    /// <summary>Gets the element capacity of this slice, equal to its requested length.</summary>
    int Capacity { get; }

    /// <summary>Gets the bytes occupied by this slice. Pool snapshots count the entire shared chunk once.</summary>
    ulong ChargedBytes { get; }
}
