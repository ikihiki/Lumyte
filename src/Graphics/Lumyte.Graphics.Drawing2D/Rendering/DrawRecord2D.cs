using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

[StructLayout(LayoutKind.Explicit, Size = 96)]
internal readonly partial record struct DrawRecord2D(
    [field: FieldOffset(0)] uint Kind, [field: FieldOffset(4)] uint Flags,
    [field: FieldOffset(8)] uint PaintId, [field: FieldOffset(12)] uint ClipId,
    [field: FieldOffset(16)] Vector4 DeviceBounds, [field: FieldOffset(32)] Vector4 InverseX,
    [field: FieldOffset(48)] Vector4 InverseY, [field: FieldOffset(64)] Vector4 Shape0,
    [field: FieldOffset(80)] uint PayloadOffset16, [field: FieldOffset(84)] uint PayloadCount,
    [field: FieldOffset(88)] uint Mode, [field: FieldOffset(92)] uint ExtraBits) : IShaderNumericWireData;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly partial record struct DrawDirectoryEntry2D(
    [field: FieldOffset(0)] uint Kind, [field: FieldOffset(4)] uint PayloadOffset16,
    [field: FieldOffset(8)] uint PayloadLength16, [field: FieldOffset(12)] uint Flags) : IShaderNumericWireData;

internal readonly partial record struct DrawPageMetadata2D(
    IGpuRef<DrawDirectoryEntry2D> PaintDirectory,
    IGpuRef<DrawDirectoryEntry2D> ClipDirectory,
    IGpuRef<Vector4> NumericPayload,
    IGpuRef<MaskDescriptor2D> MaskDescriptors,
    IGpuRef<ImagePaintData2D> ImagePaintData) : IShaderData;

internal readonly partial record struct DrawRangeArguments2D(
    IGpuRef<DrawRecord2D> Records,
    IGpuRef<DrawPageMetadata2D> PageMetadata,
    uint FirstRecord, uint RecordCount,
    Vector2 TargetSize, Vector2 PixelOrigin,
    uint Reserved0, uint Reserved1) : IShaderArguments;

internal readonly partial record struct MaskDescriptor2D(
    Vector4 Source, Vector4 TextureSizeAndRange,
    uint Encoding, uint Reserved0, uint Reserved1, uint Reserved2,
    IGpuRef<IGraphicsTextureView> Texture,
    IGpuRef<IGraphicsSampler> Sampler) : IShaderData;

internal readonly partial record struct ImagePaintData2D(
    Vector4 InverseX, Vector4 InverseY, Vector4 Source, Vector4 TextureSizeAndRange,
    uint ChildId, uint TileX, uint TileY, uint Filter,
    uint Alpha, uint FieldMode, uint Reserved0, uint Reserved1,
    IGpuRef<IGraphicsTextureView> Texture,
    IGpuRef<IGraphicsSampler> Sampler) : IShaderData;

internal readonly partial record struct DrawBasicMetadata2D(
    IGpuRef<DrawDirectoryEntry2D> PaintDirectory,
    IGpuRef<DrawDirectoryEntry2D> ClipDirectory,
    IGpuRef<Vector4> NumericPayload) : IShaderData
{
    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";
}

internal readonly partial record struct DrawMaskMetadata2D(
    IGpuRef<DrawDirectoryEntry2D> PaintDirectory,
    IGpuRef<DrawDirectoryEntry2D> ClipDirectory,
    IGpuRef<Vector4> NumericPayload,
    IGpuRef<MaskDescriptor2D> MaskDescriptors) : IShaderData
{
    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";
}

internal readonly partial record struct DrawImageMetadata2D(
    IGpuRef<DrawDirectoryEntry2D> PaintDirectory,
    IGpuRef<DrawDirectoryEntry2D> ClipDirectory,
    IGpuRef<Vector4> NumericPayload,
    IGpuRef<ImagePaintData2D> ImagePaintData) : IShaderData
{
    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";
}

internal readonly partial record struct DrawRangeArguments2D<TMetadata>(
    IGpuRef<DrawRecord2D> Records,
    IGpuRef<TMetadata> PageMetadata,
    uint FirstRecord, uint RecordCount,
    Vector2 TargetSize, Vector2 PixelOrigin,
    uint Reserved0, uint Reserved1) : IShaderArguments
    where TMetadata : struct, IShaderData
{
    static string IShaderArguments.ShaderTypeName => "DrawRangeArguments2D";

    void IShaderArguments.Write<TWriter>(ref TWriter writer)
    {
        writer.WriteReference(0, nameof(Records), Records);
        writer.WriteReference(16, nameof(PageMetadata), PageMetadata);
        writer.WriteValue(32, nameof(FirstRecord), FirstRecord);
        writer.WriteValue(36, nameof(RecordCount), RecordCount);
        writer.WriteValue(40, nameof(TargetSize), TargetSize);
        writer.WriteValue(48, nameof(PixelOrigin), PixelOrigin);
        writer.WriteValue(56, nameof(Reserved0), Reserved0);
        writer.WriteValue(60, nameof(Reserved1), Reserved1);
    }
}

internal readonly partial record struct SimpleDrawRangeArguments2D(
    IGpuRef<DrawRecord2D> Records,
    IGpuRef<Vector4> Palette,
    uint FirstRecord, uint RecordCount,
    Vector2 TargetSize, Vector2 PixelOrigin,
    uint Reserved0, uint Reserved1) : IShaderArguments
{
    static string IShaderArguments.ShaderTypeName => "DrawRangeArguments2D";
}
