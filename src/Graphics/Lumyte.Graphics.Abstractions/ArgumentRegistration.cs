namespace Lumyte.Graphics.Abstractions;

/// <summary>A reservation-local handle that exposes only a real backend reference after materialization.</summary>
/// <typeparam name="T">The resource or logical element type.</typeparam>
public sealed class ArgumentRegistration<T>
{
    private readonly ArgumentTableReservation _reservation;
    private IGpuRef<T>? _reference;

    internal ArgumentRegistration(ArgumentTableReservation reservation)
    {
        _reservation = reservation;
    }

    /// <summary>Gets the native registration belonging to this draw's selected partition.</summary>
    public IGpuRef<T> Reference
    {
        get
        {
            _reservation.ValidateMaterialized();
            return _reference ?? throw new InvalidOperationException("The registration has not materialized.");
        }
    }

    internal void Resolve(object reference) => _reference = (IGpuRef<T>)reference;
}
