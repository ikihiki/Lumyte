using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal readonly partial record struct DrawRecord2D(
    uint Kind, uint Flags, uint PaintId, uint ClipId,
    Vector4 DeviceBounds, Vector4 InverseX, Vector4 InverseY,
    Vector4 Shape0, uint PayloadOffset16, uint PayloadCount,
    uint Mode, uint ExtraBits) : IShaderData;

internal readonly partial record struct DrawDirectoryEntry2D(
    uint Kind, uint PayloadOffset16, uint PayloadLength16, uint Flags) : IShaderData;

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
        writer.WriteReference(nameof(Records), Records);
        writer.WriteReference(nameof(PageMetadata), PageMetadata);
        writer.WriteValue(nameof(FirstRecord), FirstRecord);
        writer.WriteValue(nameof(RecordCount), RecordCount);
        writer.WriteValue(nameof(TargetSize), TargetSize);
        writer.WriteValue(nameof(PixelOrigin), PixelOrigin);
        writer.WriteValue(nameof(Reserved0), Reserved0);
        writer.WriteValue(nameof(Reserved1), Reserved1);
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
