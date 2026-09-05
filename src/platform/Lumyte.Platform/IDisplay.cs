using System.Drawing;

namespace Lumyte.Platform;

public interface IDisplay
{
    string Name { get; }

    Rectangle Bounds { get; }

    Rectangle WorkArea { get; }

    float ScaleFactor { get; }

    bool IsPrimary { get; }
}
