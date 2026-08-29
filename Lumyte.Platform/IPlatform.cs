namespace Lumyte.Platform;

public interface IPlatform : IDisposable
{
    IPlatformInput Input { get; }

    IReadOnlyList<IDisplay> Displays { get; }

    IWindow CreateWindow(WindowOptions options);

    bool PumpEvents();
}
