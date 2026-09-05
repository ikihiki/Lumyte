using System.Drawing;

using Lumyte.Platform;
using Silk.NET.Windowing;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkDisplay(IMonitor monitor, bool isPrimary) : IDisplay
{
    public IMonitor Native => monitor;

    public string Name => monitor.Name;

    public Rectangle Bounds => new(
        monitor.Bounds.Origin.X,
        monitor.Bounds.Origin.Y,
        monitor.Bounds.Size.X,
        monitor.Bounds.Size.Y);

    public Rectangle WorkArea => Bounds;

    public float ScaleFactor => 1;

    public bool IsPrimary { get; } = isPrimary;
}
