namespace Lumyte.Input.Processing;

/// <summary>Shares InputSystem time with injected sources without resolving DI services during updates or disposal.</summary>
public sealed class InputTimeSource
{
    private InputSystem? _system;

    /// <summary>Gets the system's monotonic elapsed time, or zero before attachment.</summary>
    public TimeSpan ElapsedTime => _system?.ElapsedTime ?? TimeSpan.Zero;

    /// <summary>Returns elapsed time for source callbacks.</summary>
    /// <returns>The monotonic time.</returns>
    public TimeSpan GetElapsedTime() => ElapsedTime;

    /// <summary>Attaches the system once after its sources have initialized.</summary>
    /// <param name="system">The owning input system.</param>
    public void Attach(InputSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_system is not null)
        {
            throw new InvalidOperationException("An input system is already attached.");
        }

        _system = system;
    }
}
