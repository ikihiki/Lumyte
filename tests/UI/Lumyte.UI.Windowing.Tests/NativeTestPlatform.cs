using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Windowing.Tests;

internal sealed class NativeTestPlatform : IPlatform
{
    public Guid Id { get; } = Guid.NewGuid();

    public PlatformFeatures Features => PlatformFeatures.NativeWindows;

    internal Queue<Action> Pending { get; } = new();

    internal List<NativeTestWindow> Windows { get; } = [];

    internal int Pumps { get; private set; }

    public IWindow CreateWindow(WindowDescriptor descriptor)
    {
        var window = new NativeTestWindow(this, new WindowId(Id, (ulong)Windows.Count + 1), descriptor);
        Windows.Add(window);
        return window;
    }

    public IInputSource CreateInputSource() => throw new NotSupportedException();

    public IClipboard CreateClipboard() => throw new NotSupportedException();

    public void PumpEvents()
    {
        Pumps++;
        while (Pending.TryDequeue(out Action? action))
        {
            action();
        }

        foreach (NativeTestWindow window in Windows)
        {
            window.Advance();
        }
    }

    public void Dispose()
    {
    }
}
