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
    [field: FieldOffset(88)] uint Mode, [field: FieldOffset(92)] uint ExtraBits) : IShaderArguments;

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal readonly partial record struct DrawDirectoryEntry2D(
    [field: FieldOffset(0)] uint Kind, [field: FieldOffset(4)] uint PayloadOffset16,
    [field: FieldOffset(8)] uint PayloadLength16, [field: FieldOffset(12)] uint Flags) : IShaderArguments;

internal partial record struct DrawPageMetadata2D : IShaderArguments
{
    private TransferData _data;

    public DrawPageMetadata2D(IGpuRef<DrawDirectoryEntry2D> paintDirectory, IGpuRef<DrawDirectoryEntry2D> clipDirectory, IGpuRef<Vector4> numericPayload, IGpuRef<MaskDescriptor2D> maskDescriptors, IGpuRef<ImagePaintData2D> imagePaintData)
    {
        _data = default;
        PaintDirectory = paintDirectory;
        ClipDirectory = clipDirectory;
        NumericPayload = numericPayload;
        MaskDescriptors = maskDescriptors;
        ImagePaintData = imagePaintData;
    }

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; }

    public IGpuRef<DrawDirectoryEntry2D> ClipDirectory { get; set; }

    public IGpuRef<Vector4> NumericPayload { get; set; }

    public IGpuRef<MaskDescriptor2D> MaskDescriptors { get; set; }

    public IGpuRef<ImagePaintData2D> ImagePaintData { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot ClipDirectory;
        public ShaderReferenceSlot NumericPayload;
        public ShaderReferenceSlot MaskDescriptors;
        public ShaderReferenceSlot ImagePaintData;
    }
}

internal partial record struct DrawRangeArguments2D : IShaderArguments
{
    private TransferData _data;

    public DrawRangeArguments2D(IGpuRef<DrawRecord2D> records, IGpuRef<DrawPageMetadata2D> pageMetadata, uint firstRecord, uint recordCount, Vector2 targetSize, Vector2 pixelOrigin, uint reserved0, uint reserved1)
    {
        _data = default;
        Records = records;
        PageMetadata = pageMetadata;
        FirstRecord = firstRecord;
        RecordCount = recordCount;
        TargetSize = targetSize;
        PixelOrigin = pixelOrigin;
        Reserved0 = reserved0;
        Reserved1 = reserved1;
    }

    public IGpuRef<DrawRecord2D> Records { get; set; }

    public IGpuRef<DrawPageMetadata2D> PageMetadata { get; set; }

    public partial uint FirstRecord { get; set; }

    public partial uint RecordCount { get; set; }

    public partial Vector2 TargetSize { get; set; }

    public partial Vector2 PixelOrigin { get; set; }

    public partial uint Reserved0 { get; set; }

    public partial uint Reserved1 { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Records;
        public ShaderReferenceSlot PageMetadata;
        public uint FirstRecord;
        public uint RecordCount;
        public Vector2 TargetSize;
        public Vector2 PixelOrigin;
        public uint Reserved0;
        public uint Reserved1;
    }
}

internal partial record struct MaskDescriptor2D : IShaderArguments
{
    private TransferData _data;

    public MaskDescriptor2D(Vector4 source, Vector4 textureSizeAndRange, uint encoding, uint reserved0, uint reserved1, uint reserved2, IGpuRef<IGraphicsTextureView> texture, IGpuRef<IGraphicsSampler> sampler)
    {
        _data = default;
        Source = source;
        TextureSizeAndRange = textureSizeAndRange;
        Encoding = encoding;
        Reserved0 = reserved0;
        Reserved1 = reserved1;
        Reserved2 = reserved2;
        Texture = texture;
        Sampler = sampler;
    }

    public partial Vector4 Source { get; set; }

    public partial Vector4 TextureSizeAndRange { get; set; }

    public partial uint Encoding { get; set; }

    public partial uint Reserved0 { get; set; }

    public partial uint Reserved1 { get; set; }

    public partial uint Reserved2 { get; set; }

    public IGpuRef<IGraphicsTextureView> Texture { get; set; }

    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Vector4 Source;
        public Vector4 TextureSizeAndRange;
        public uint Encoding;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}

internal partial record struct ImagePaintData2D : IShaderArguments
{
    private TransferData _data;

    public ImagePaintData2D(Vector4 inverseX, Vector4 inverseY, Vector4 source, Vector4 textureSizeAndRange, uint childId, uint tileX, uint tileY, uint filter, uint alpha, uint fieldMode, uint reserved0, uint reserved1, IGpuRef<IGraphicsTextureView> texture, IGpuRef<IGraphicsSampler> sampler)
    {
        _data = default;
        InverseX = inverseX;
        InverseY = inverseY;
        Source = source;
        TextureSizeAndRange = textureSizeAndRange;
        ChildId = childId;
        TileX = tileX;
        TileY = tileY;
        Filter = filter;
        Alpha = alpha;
        FieldMode = fieldMode;
        Reserved0 = reserved0;
        Reserved1 = reserved1;
        Texture = texture;
        Sampler = sampler;
    }

    public partial Vector4 InverseX { get; set; }

    public partial Vector4 InverseY { get; set; }

    public partial Vector4 Source { get; set; }

    public partial Vector4 TextureSizeAndRange { get; set; }

    public partial uint ChildId { get; set; }

    public partial uint TileX { get; set; }

    public partial uint TileY { get; set; }

    public partial uint Filter { get; set; }

    public partial uint Alpha { get; set; }

    public partial uint FieldMode { get; set; }

    public partial uint Reserved0 { get; set; }

    public partial uint Reserved1 { get; set; }

    public IGpuRef<IGraphicsTextureView> Texture { get; set; }

    public IGpuRef<IGraphicsSampler> Sampler { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public Vector4 InverseX;
        public Vector4 InverseY;
        public Vector4 Source;
        public Vector4 TextureSizeAndRange;
        public uint ChildId;
        public uint TileX;
        public uint TileY;
        public uint Filter;
        public uint Alpha;
        public uint FieldMode;
        public uint Reserved0;
        public uint Reserved1;
        public ShaderReferenceSlot Texture;
        public ShaderReferenceSlot Sampler;
    }
}

internal partial record struct DrawBasicMetadata2D : IShaderArguments
{
    private TransferData _data;

    public DrawBasicMetadata2D(IGpuRef<DrawDirectoryEntry2D> paintDirectory, IGpuRef<DrawDirectoryEntry2D> clipDirectory, IGpuRef<Vector4> numericPayload)
    {
        _data = default;
        PaintDirectory = paintDirectory;
        ClipDirectory = clipDirectory;
        NumericPayload = numericPayload;
    }

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; }

    public IGpuRef<DrawDirectoryEntry2D> ClipDirectory { get; set; }

    public IGpuRef<Vector4> NumericPayload { get; set; }

    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot ClipDirectory;
        public ShaderReferenceSlot NumericPayload;
    }
}

internal partial record struct DrawMaskMetadata2D : IShaderArguments
{
    private TransferData _data;

    public DrawMaskMetadata2D(IGpuRef<DrawDirectoryEntry2D> paintDirectory, IGpuRef<DrawDirectoryEntry2D> clipDirectory, IGpuRef<Vector4> numericPayload, IGpuRef<MaskDescriptor2D> maskDescriptors)
    {
        _data = default;
        PaintDirectory = paintDirectory;
        ClipDirectory = clipDirectory;
        NumericPayload = numericPayload;
        MaskDescriptors = maskDescriptors;
    }

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; }

    public IGpuRef<DrawDirectoryEntry2D> ClipDirectory { get; set; }

    public IGpuRef<Vector4> NumericPayload { get; set; }

    public IGpuRef<MaskDescriptor2D> MaskDescriptors { get; set; }

    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot ClipDirectory;
        public ShaderReferenceSlot NumericPayload;
        public ShaderReferenceSlot MaskDescriptors;
    }
}

internal partial record struct DrawImageMetadata2D : IShaderArguments
{
    private TransferData _data;

    public DrawImageMetadata2D(IGpuRef<DrawDirectoryEntry2D> paintDirectory, IGpuRef<DrawDirectoryEntry2D> clipDirectory, IGpuRef<Vector4> numericPayload, IGpuRef<ImagePaintData2D> imagePaintData)
    {
        _data = default;
        PaintDirectory = paintDirectory;
        ClipDirectory = clipDirectory;
        NumericPayload = numericPayload;
        ImagePaintData = imagePaintData;
    }

    public IGpuRef<DrawDirectoryEntry2D> PaintDirectory { get; set; }

    public IGpuRef<DrawDirectoryEntry2D> ClipDirectory { get; set; }

    public IGpuRef<Vector4> NumericPayload { get; set; }

    public IGpuRef<ImagePaintData2D> ImagePaintData { get; set; }

    static string IShaderArguments.ShaderTypeName => "DrawPageMetadata2D";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot PaintDirectory;
        public ShaderReferenceSlot ClipDirectory;
        public ShaderReferenceSlot NumericPayload;
        public ShaderReferenceSlot ImagePaintData;
    }
}

internal partial record struct DrawRangeArguments2D<TMetadata> : IShaderArguments
    where TMetadata : struct, IShaderArguments
{
    private TransferData _data;

    public DrawRangeArguments2D(IGpuRef<DrawRecord2D> records, IGpuRef<TMetadata> pageMetadata, uint firstRecord, uint recordCount, Vector2 targetSize, Vector2 pixelOrigin, uint reserved0, uint reserved1)
    {
        _data = default;
        Records = records;
        PageMetadata = pageMetadata;
        FirstRecord = firstRecord;
        RecordCount = recordCount;
        TargetSize = targetSize;
        PixelOrigin = pixelOrigin;
        Reserved0 = reserved0;
        Reserved1 = reserved1;
    }

    public IGpuRef<DrawRecord2D> Records { get; set; }

    public IGpuRef<TMetadata> PageMetadata { get; set; }

    public uint FirstRecord { readonly get => _data.FirstRecord; set => _data.FirstRecord = value; }

    public uint RecordCount { readonly get => _data.RecordCount; set => _data.RecordCount = value; }

    public Vector2 TargetSize { readonly get => _data.TargetSize; set => _data.TargetSize = value; }

    public Vector2 PixelOrigin { readonly get => _data.PixelOrigin; set => _data.PixelOrigin = value; }

    public uint Reserved0 { readonly get => _data.Reserved0; set => _data.Reserved0 = value; }

    public uint Reserved1 { readonly get => _data.Reserved1; set => _data.Reserved1 = value; }

    static string IShaderArguments.RootParameter => "arguments";

    static string IShaderArguments.ShaderTypeName => "DrawRangeArguments2D";

    static int IShaderArguments.TransferSize => 64;

    static int IShaderArguments.ReferenceCount => 2;

    static IReadOnlyList<ShaderTransferField> IShaderArguments.TransferFields => [
        new("Records", 0, 16, typeof(IGpuRef<DrawRecord2D>), true),
        new("PageMetadata", 16, 16, typeof(IGpuRef<TMetadata>), true),
        new("FirstRecord", 32, 4, typeof(uint), false),
        new("RecordCount", 36, 4, typeof(uint), false),
        new("TargetSize", 40, 8, typeof(Vector2), false),
        new("PixelOrigin", 48, 8, typeof(Vector2), false),
        new("Reserved0", 56, 4, typeof(uint), false),
        new("Reserved1", 60, 4, typeof(uint), false),
    ];

    readonly void IShaderArguments.Write<TWriter>(ref TWriter writer)
    {
        writer.WriteReference(Records);
        writer.WriteReference(PageMetadata);
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in _data, 1))[32..]);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Records;
        public ShaderReferenceSlot PageMetadata;
        public uint FirstRecord;
        public uint RecordCount;
        public Vector2 TargetSize;
        public Vector2 PixelOrigin;
        public uint Reserved0;
        public uint Reserved1;
    }
}

internal partial record struct SimpleDrawRangeArguments2D : IShaderArguments
{
    private TransferData _data;

    public SimpleDrawRangeArguments2D(IGpuRef<DrawRecord2D> records, IGpuRef<Vector4> palette, uint firstRecord, uint recordCount, Vector2 targetSize, Vector2 pixelOrigin, uint reserved0, uint reserved1)
    {
        _data = default;
        Records = records;
        Palette = palette;
        FirstRecord = firstRecord;
        RecordCount = recordCount;
        TargetSize = targetSize;
        PixelOrigin = pixelOrigin;
        Reserved0 = reserved0;
        Reserved1 = reserved1;
    }

    public IGpuRef<DrawRecord2D> Records { get; set; }

    public IGpuRef<Vector4> Palette { get; set; }

    public partial uint FirstRecord { get; set; }

    public partial uint RecordCount { get; set; }

    public partial Vector2 TargetSize { get; set; }

    public partial Vector2 PixelOrigin { get; set; }

    public partial uint Reserved0 { get; set; }

    public partial uint Reserved1 { get; set; }

    static string IShaderArguments.ShaderTypeName => "DrawRangeArguments2D";

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TransferData
    {
        public ShaderReferenceSlot Records;
        public ShaderReferenceSlot Palette;
        public uint FirstRecord;
        public uint RecordCount;
        public Vector2 TargetSize;
        public Vector2 PixelOrigin;
        public uint Reserved0;
        public uint Reserved1;
    }
}
