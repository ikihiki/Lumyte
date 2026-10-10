namespace Lumyte.Input;

/// <summary>Registers and unregisters devices discovered by a platform backend.</summary>
public interface IInputSource : IDisposable
{
    /// <summary>Attaches the source to its scoped device registry.</summary>
    /// <param name="registry">The source-specific registry.</param>
    void Initialize(IInputDeviceRegistry registry);

    /// <summary>Processes pending device connections and disconnections.</summary>
    void Update();

    /// <summary>Stops discovery without disposing devices or discarding pending input.</summary>
    void Shutdown();
}
