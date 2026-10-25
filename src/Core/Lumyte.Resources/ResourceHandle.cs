using System.Runtime.InteropServices;

namespace Lumyte.Resources;

/// <summary>Identifies a logical resource without owning it or containing managed references.</summary>
/// <typeparam name="T">The exact resource output type, which is not stored in this value.</typeparam>
/// <remarks>
/// Copying this 16-byte handle does not extend the resource lifetime. A resource reference owns its
/// slot, while a lease obtained from the manager protects a particular content revision.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct ResourceHandle<T> : IEquatable<ResourceHandle<T>>
    where T : notnull
{
    internal ResourceHandle(ulong managerId, uint slot, uint generation)
    {
        (ManagerId, Slot, Generation) = (managerId, slot, generation);
    }

    /// <summary>Gets the process-local manager identifier.</summary>
    public ulong ManagerId { get; }

    /// <summary>Gets the logical resource slot within the manager.</summary>
    public uint Slot { get; }

    /// <summary>Gets the slot generation, which changes when the slot is reused.</summary>
    public uint Generation { get; }

    /// <summary>Gets a value indicating whether this is an uninitialized handle.</summary>
    /// <remarks>This check does not validate whether a nondefault handle is still alive.</remarks>
    public bool IsDefault => ManagerId == 0 || Generation == 0;

    /// <summary>Determines whether two handles identify the same manager, slot and generation.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns>Whether both handles contain the same identifiers.</returns>
    public static bool operator ==(ResourceHandle<T> left, ResourceHandle<T> right) => left.Equals(right);

    /// <summary>Determines whether two handles identify different manager, slot or generation values.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns>Whether either handle contains a different identifier.</returns>
    public static bool operator !=(ResourceHandle<T> left, ResourceHandle<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(ResourceHandle<T> other) => ManagerId == other.ManagerId && Slot == other.Slot && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ResourceHandle<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ManagerId, Slot, Generation);
}
