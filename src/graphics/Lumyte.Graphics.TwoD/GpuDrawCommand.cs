using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuDrawCommand
{
    public const int Size = 128;

    public Vector4 Header;
    public Vector4 Bounds;
    public Vector4 Color;
    public Vector4 Parameters0;
    public Vector4 Parameters1;
    public Vector4 TextureRegion;
    public Vector4 Transform0;
    public Vector4 Transform1;
}
