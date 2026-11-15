using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed record RecordedDrawRange(IGpuRef<DrawRecord2D> Records, IShaderReference PageMetadata, uint FirstRecord, uint RecordCount, Vector2 TargetSize, Vector2 PixelOrigin, uint Reserved0, uint Reserved1)
{
    internal static RecordedDrawRange Capture(ShaderValueSnapshot snapshot)
    {
        uint UInt(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Data.Span[offset..]);
        return new((IGpuRef<DrawRecord2D>)snapshot.Values.Single(value => value.Offset == 0).Reference!, (IShaderReference)snapshot.Values.Single(value => value.Offset == 16).Reference!, UInt(32), UInt(36), MemoryMarshal.Read<Vector2>(snapshot.Data.Span[40..]), MemoryMarshal.Read<Vector2>(snapshot.Data.Span[48..]), UInt(56), UInt(60));
    }
}
