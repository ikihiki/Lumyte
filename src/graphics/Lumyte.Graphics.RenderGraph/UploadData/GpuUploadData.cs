namespace Lumyte.Graphics.RenderGraph;

public readonly record struct GpuUploadDataKey(string Id, long Revision);
public readonly record struct GpuUploadProfileId(string Name, int Version);
public interface IGpuUploadData { GpuUploadDataKey Key { get; } }
public enum GpuImageColorEncoding { Linear, Srgb, Data }
public enum GpuImageAlphaMode { Opaque, Straight, Premultiplied }
public enum GpuPackageUploadExportKind { Buffer, Image }

public sealed class GpuBufferUploadData : IGpuUploadData
{
    public GpuBufferUploadData(GpuUploadDataKey key, ReadOnlyMemory<byte> data) { Key = key; Data = data.ToArray(); }
    public GpuUploadDataKey Key { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

public sealed class GpuImageSubresourceData
{
    public GpuImageSubresourceData(uint mipLevel, uint arrayLayer, ulong rowStride, ulong sliceStride, ReadOnlyMemory<byte> data)
    { MipLevel = mipLevel; ArrayLayer = arrayLayer; RowStride = rowStride; SliceStride = sliceStride; Data = data.ToArray(); }
    public uint MipLevel { get; }
    public uint ArrayLayer { get; }
    public ulong RowStride { get; }
    public ulong SliceStride { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

public sealed class GpuImageUploadData : IGpuUploadData
{
    public GpuImageUploadData(GpuUploadDataKey key, GpuGraphTextureDescription description,
        GpuImageColorEncoding encoding, GpuImageAlphaMode alphaMode, IEnumerable<GpuImageSubresourceData> subresources)
    { Key = key; Description = description; Encoding = encoding; AlphaMode = alphaMode; Subresources = Array.AsReadOnly(subresources.ToArray()); }
    public GpuUploadDataKey Key { get; }
    public GpuGraphTextureDescription Description { get; }
    public GpuImageColorEncoding Encoding { get; }
    public GpuImageAlphaMode AlphaMode { get; }
    public IReadOnlyList<GpuImageSubresourceData> Subresources { get; }
}

public sealed record GpuPackageUploadExport(string Name, int Index, GpuPackageUploadExportKind Kind)
{
    public static GpuPackageUploadExport Buffer(string name, int index) => new(name, index, GpuPackageUploadExportKind.Buffer);
    public static GpuPackageUploadExport Image(string name, int index) => new(name, index, GpuPackageUploadExportKind.Image);
}

public sealed class GpuPackageUploadData : IGpuUploadData
{
    public GpuPackageUploadData(GpuUploadDataKey key, IEnumerable<GpuBufferUploadData> buffers,
        IEnumerable<GpuImageUploadData> images, IEnumerable<GpuPackageUploadExport> exports, GpuUploadProfileId profile)
    { Key = key; Buffers = Array.AsReadOnly(buffers.ToArray()); Images = Array.AsReadOnly(images.ToArray()); Exports = Array.AsReadOnly(exports.ToArray()); Profile = profile; }
    public GpuUploadDataKey Key { get; }
    public IReadOnlyList<GpuBufferUploadData> Buffers { get; }
    public IReadOnlyList<GpuImageUploadData> Images { get; }
    public IReadOnlyList<GpuPackageUploadExport> Exports { get; }
    public GpuUploadProfileId Profile { get; }
}
