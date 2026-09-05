namespace Lumyte.Platform;

public sealed class WindowInputChangedEventArgs(IWindowInput input, bool isAdded) : EventArgs
{
    public IWindowInput Input { get; } = input;

    public bool IsAdded { get; } = isAdded;
}
