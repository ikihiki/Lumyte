namespace Lumyte.Graphics;

/// <summary>
/// Represents a non-owning typed data range without exposing addresses or binding slots.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public readonly struct GpuReference<T>
    where T : unmanaged
{
    internal GpuReference(object handle)
    {
        Handle = handle;
    }

    internal object? Handle { get; }

    /// <summary>
    /// Returns a diagnostic type name without revealing the internal GPU representation.
    /// </summary>
    /// <returns>The diagnostic type name without an address or binding index.</returns>
    public override string ToString() => $"GpuReference<{typeof(T).Name}>";
}
