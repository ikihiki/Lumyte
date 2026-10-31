using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Direct3D12;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTextureRegion
{
    internal uint Subresource;
    internal uint X;
    internal uint Y;
    internal uint Width;
    internal uint Height;
}
