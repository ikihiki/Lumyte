#include "lumyte.slang"
struct DrawRecord2D {
    uint Kind; uint Flags; uint PaintId; uint ClipId;
    float4 DeviceBounds; float4 InverseX; float4 InverseY; float4 Shape0;
    uint PayloadOffset16; uint PayloadCount; uint Mode; uint ExtraBits;
};
struct DrawDirectoryEntry2D { uint Kind; uint PayloadOffset16; uint PayloadLength16; uint Flags; };
struct MaskDescriptor2D {
    float4 Source; float4 TextureSizeAndRange;
    uint Encoding; uint Reserved0; uint Reserved1; uint Reserved2;
    GpuTextureRef Texture; GpuSamplerRef Sampler;
};
struct ImagePaintData2D {
    float4 InverseX; float4 InverseY; float4 Source; float4 TextureSizeAndRange;
    uint ChildId; uint TileX; uint TileY; uint Filter;
    uint Alpha; uint FieldMode; uint Reserved0; uint Reserved1;
    GpuTextureRef Texture; GpuSamplerRef Sampler;
};
struct DrawPageMetadata2D {
    GpuBufferRef<DrawDirectoryEntry2D> PaintDirectory;
    GpuBufferRef<DrawDirectoryEntry2D> ClipDirectory;
    GpuBufferRef<float4> NumericPayload;
    GpuBufferRef<MaskDescriptor2D> MaskDescriptors;
    GpuBufferRef<ImagePaintData2D> ImagePaintData;
};
struct DrawRangeArguments2D {
    GpuBufferRef<DrawRecord2D> Records;
    GpuBufferRef<DrawPageMetadata2D> PageMetadata;
    uint FirstRecord; uint RecordCount;
    float2 TargetSize; float2 PixelOrigin; uint Reserved0; uint Reserved1;
};
ConstantBuffer<DrawRangeArguments2D> arguments;

struct Output { float4 Position : SV_Position; nointerpolation uint Index : TEXCOORD0; };
[shader("vertex")]
Output main(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    float2 corners[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
    uint index = arguments.FirstRecord + min(instance, arguments.RecordCount - 1);
    DrawRecord2D record = arguments.Records.Load(min(index, arguments.Records.Count - 1));
    float2 pixel = lerp(record.DeviceBounds.xy, record.DeviceBounds.zw, corners[vertex]);
    Output output;
    output.Position = float4(pixel.x * 2 / arguments.TargetSize.x - 1, 1 - pixel.y * 2 / arguments.TargetSize.y, 0, 1);
    output.Index = index;
    return output;
}
