using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuLayerCommand
{
    public Vector4 Settings;
    public Vector4 Tint;
    public Vector4 OffsetAndTargetSize;
    public Vector4 Direction;

    public const int Size = 64;
}
