using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPolygonHeader
{
    public const int Size = 64;

    public Vector4 Color;
    public Vector4 Transform0;
    public Vector4 Transform1;
    public Vector4 Reserved;
}
