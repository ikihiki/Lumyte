#include "lumyte.slang"
#ifndef LUMYTE_RECORD_MASKS
#define LUMYTE_RECORD_MASKS 1
#endif
#ifndef LUMYTE_RECORD_IMAGES
#define LUMYTE_RECORD_IMAGES 1
#endif
#ifndef LUMYTE_SIMPLE_RECORDS
#define LUMYTE_SIMPLE_RECORDS 0
#endif
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

#if LUMYTE_RECORD_MASKS
    GpuBufferRef<MaskDescriptor2D> MaskDescriptors;
#endif
#if LUMYTE_RECORD_IMAGES
    GpuBufferRef<ImagePaintData2D> ImagePaintData;
#endif
};
struct DrawRangeArguments2D {
    GpuBufferRef<DrawRecord2D> Records;

#if LUMYTE_SIMPLE_RECORDS
    GpuBufferRef<float4> Palette;
#else
    GpuBufferRef<DrawPageMetadata2D> PageMetadata;
#endif
    uint FirstRecord; uint RecordCount;
    float2 TargetSize; float2 PixelOrigin; uint Reserved0; uint Reserved1;
};
ConstantBuffer<DrawRangeArguments2D> arguments;

struct Output { float4 Position : SV_Position; nointerpolation uint Index : TEXCOORD0; };
// One record per workgroup. The fragment evaluator owns correlated coverage.
[shader("mesh")]
[numthreads(1,1,1)]
[outputtopology("triangle")]
void main(uint3 group : SV_GroupID, out vertices Output vertices[4], out indices uint3 triangles[2])
{
    SetMeshOutputCounts(4, 2);
    float2 corners[4] = { float2(0,0), float2(1,0), float2(0,1), float2(1,1) };
    uint index = arguments.FirstRecord + min(group.x, arguments.RecordCount - 1);
    DrawRecord2D record = arguments.Records.Load(min(index, arguments.Records.Count - 1));
    for (uint i = 0; i < 4; i++)
    {
        float2 pixel = lerp(record.DeviceBounds.xy, record.DeviceBounds.zw, corners[i]);
        pixel += float2(asfloat(arguments.Reserved0), asfloat(arguments.Reserved1));
        vertices[i].Position = float4(pixel.x * 2 / arguments.TargetSize.x - 1, 1 - pixel.y * 2 / arguments.TargetSize.y, 0, 1);
        vertices[i].Index = index;
    }
    triangles[0] = uint3(0,1,2);
    triangles[1] = uint3(2,1,3);
}
