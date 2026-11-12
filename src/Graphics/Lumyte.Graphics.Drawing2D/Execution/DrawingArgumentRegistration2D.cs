using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A compatibility handle for a single draw reservation; native references remain opaque.</summary>
/// <typeparam name="T">The referenced resource or element type.</typeparam>
public sealed class DrawingArgumentRegistration2D<T>
{
    private readonly ArgumentRegistration<T> _registration;

    internal DrawingArgumentRegistration2D(ArgumentRegistration<T> registration)
    {
        _registration = registration;
    }

    /// <summary>Gets the real backend reference after all partition registrations succeed.</summary>
    public IGpuRef<T> Reference => _registration.Reference;
}
