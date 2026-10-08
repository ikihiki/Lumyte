namespace Lumyte.Graphics;

/// <summary>Describes the reflected wire layout of a caller-owned logical element.</summary>
/// <typeparam name="T">The caller-owned logical element type.</typeparam>
public sealed class ShaderDataLayout<T>
{
    internal ShaderDataLayout(IGraphicsDriver driver, object handle, ulong stride)
    {
        (Driver, Handle, ElementStrideInBytes) = (driver, handle, stride);
    }

    /// <summary>Gets the backend-resolved wire stride without implicit alignment correction.</summary>
    public ulong ElementStrideInBytes { get; }

    internal IGraphicsDriver Driver { get; }

    internal object Handle { get; }

    /// <summary>Computes the exact checked wire byte size.</summary>
    /// <param name="count">The positive logical element count.</param>
    /// <returns>The exact byte size.</returns>
    public ulong GetSizeInBytes(ulong count) => count != 0 ? checked(count * ElementStrideInBytes) : throw new ArgumentOutOfRangeException(nameof(count));
}
