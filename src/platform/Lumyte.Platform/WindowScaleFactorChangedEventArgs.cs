namespace Lumyte.Platform;

public sealed class WindowScaleFactorChangedEventArgs(float scaleFactor) : EventArgs
{
    public float ScaleFactor { get; } = scaleFactor;
}
