namespace Lumyte.Platform.Tests;

internal sealed class TestPlatform : IPlatform
{
    public Guid Id { get; } = Guid.NewGuid();

    public PlatformFeatures Features => PlatformFeatures.NativeWindows;

    internal Queue<Action> Pending { get; } = new();

    internal List<TestWindow> Windows { get; } = [];

    internal int Pumps { get; private set; }

    public IWindow CreateWindow(WindowDescriptor descriptor)
    {
        var window = new TestWindow(this, new WindowId(Id, (ulong)Windows.Count + 1), descriptor);
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

        foreach (TestWindow window in Windows)
        {
            window.Advance();
        }
    }

    public void Dispose()
    {
    }
}
