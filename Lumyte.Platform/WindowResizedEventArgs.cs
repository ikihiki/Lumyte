using System.Drawing;

namespace Lumyte.Platform;

public sealed class WindowResizedEventArgs(Size clientSize) : EventArgs
{
    public Size ClientSize { get; } = clientSize;
}
