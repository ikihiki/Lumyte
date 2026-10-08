namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides immutable capabilities of a backend-owned graphics device.</summary>
public interface IGraphicDevice
{
    /// <summary>Gets the immutable device capabilities.</summary>
    public DeviceCaps Caps { get; }

    /// <summary>Gets the raw element layout and GPU copy alignment expressed for T.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <returns>The backend-resolved layout without implicit padding.</returns>
    BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged;

    /// <summary>Creates an owned typed buffer using the exact requested element count.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="desc">The immutable count, usage and memory requirements.</param>
    /// <returns>The concrete backend allocation through its common interface.</returns>
    IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged;
}
