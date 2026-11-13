using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed record RecordedDrawRange(IGpuRef<DrawRecord2D> Records, IShaderReference PageMetadata, uint FirstRecord, uint RecordCount, Vector2 TargetSize, Vector2 PixelOrigin, uint Reserved0, uint Reserved1)
{
    internal static RecordedDrawRange Capture(ShaderValueSnapshot snapshot)
    {
        ShaderValue Field(string name) => snapshot.Values.Single(value => value.Path == name);
        uint UInt(string name) => BinaryPrimitives.ReadUInt32LittleEndian(Field(name).Data.Span);
        return new((IGpuRef<DrawRecord2D>)Field("Records").Reference!, (IShaderReference)snapshot.Values.Single(value => value.IsReference && value.Path != "Records").Reference!, UInt("FirstRecord"), UInt("RecordCount"), MemoryMarshal.Read<Vector2>(Field("TargetSize").Data.Span), MemoryMarshal.Read<Vector2>(Field("PixelOrigin").Data.Span), UInt("Reserved0"), UInt("Reserved1"));
    }
}
