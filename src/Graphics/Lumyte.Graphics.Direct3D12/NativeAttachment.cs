using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAttachment
{
    internal nint Resource;
    internal uint Format;
    internal uint Mip;
    internal uint Layer;
    internal uint Clear;
    internal float R;
    internal float G;
    internal float B;
    internal float A;
}
