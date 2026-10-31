#ifndef LUMYTE_SHADER_ABI
#define LUMYTE_SHADER_ABI

struct GpuTextureRef { uint Id; uint Offset; uint Count; uint Reserved; };
struct GpuSamplerRef { uint Id; uint Offset; uint Count; uint Reserved; };

#ifdef LUMYTE_DXIL
Texture2D<float4> lumyteTextures[128] : register(t0, space0);
SamplerState lumyteSamplers[16] : register(s0, space0);
ByteAddressBuffer lumyteBuffers[64] : register(t0, space1);
RWByteAddressBuffer lumyteRWBuffers[64] : register(u0, space1);
struct LumyteDrawInfo { uint Kind; uint BaseVertex; };
ConstantBuffer<LumyteDrawInfo> lumyteDrawInfo : register(b9, space0);
ByteAddressBuffer lumyteDrawArguments : register(t0, space2);

// SM 6.6 has no SV_StartVertexLocation. Read indirect offsets on the GPU,
// so GPU-generated commands do not require a CPU readback or a changed command layout.
uint LumyteVertexIndex(uint vertexId)
{
    uint baseVertex = lumyteDrawInfo.Kind == 0 ? lumyteDrawInfo.BaseVertex
        : lumyteDrawArguments.Load(lumyteDrawInfo.Kind == 1 ? 8 : 12);
    return vertexId + baseVertex;
}
#elif defined(LUMYTE_SPIRV)
[[vk::binding(0, 0)]] Texture2D<float4> lumyteTextures[];
[[vk::binding(1, 0)]] SamplerState lumyteSamplers[];
#else
[[vk::binding(0, 0)]] Texture2D<float4> lumyteTexture0;
[[vk::binding(1, 0)]] SamplerState lumyteSampler0;
[[vk::binding(2, 0)]] ByteAddressBuffer lumyteBuffer0;
[[vk::binding(3, 0)]] RWByteAddressBuffer lumyteRWBuffer0;
[[vk::binding(9, 0)]] StructuredBuffer<uint4> lumyteMap;
#endif

// T uses Slang's natural layout for pointer access and raw byte-address loads.
// Offline and online schema probes reflect Ptr<T> to preserve that exact layout.
struct GpuBufferRef<T>
{
    uint Id;
    uint Offset;
    uint Count;
    uint Reserved;

    T Load(uint index)
    {
#ifdef LUMYTE_SPIRV
        uint64_t address = uint64_t(Id) | (uint64_t(Offset) << 32);
        return ((T*)address)[index];
#else
#ifdef LUMYTE_DXIL
        return lumyteBuffers[NonUniformResourceIndex(Id)].Load<T>(Offset + index * sizeof(T));
#else
        return lumyteBuffer0.Load<T>(Offset + index * sizeof(T));
#endif
#endif
    }
};

struct GpuRWBufferRef<T>
{
    uint Id;
    uint Offset;
    uint Count;
    uint Reserved;

    T Load(uint index)
    {
#ifdef LUMYTE_SPIRV
        uint64_t address = uint64_t(Id) | (uint64_t(Offset) << 32);
        return ((T*)address)[index];
#else
#ifdef LUMYTE_DXIL
        return lumyteRWBuffers[NonUniformResourceIndex(Id)].Load<T>(Offset + index * sizeof(T));
#else
        return lumyteRWBuffer0.Load<T>(Offset + index * sizeof(T));
#endif
#endif
    }

    void Store(uint index, T value)
    {
#ifdef LUMYTE_SPIRV
        uint64_t address = uint64_t(Id) | (uint64_t(Offset) << 32);
        ((T*)address)[index] = value;
#else
#ifdef LUMYTE_DXIL
        lumyteRWBuffers[NonUniformResourceIndex(Id)].Store<T>(Offset + index * sizeof(T), value);
#else
        lumyteRWBuffer0.Store<T>(Offset + index * sizeof(T), value);
#endif
#endif
    }
};

float4 LumyteSampleGrad(GpuTextureRef textureRef, GpuSamplerRef samplerRef, float2 uv, float2 dx, float2 dy)
{
#if defined(LUMYTE_SPIRV) || defined(LUMYTE_DXIL)
    return lumyteTextures[NonUniformResourceIndex(textureRef.Id)].SampleGrad(
        lumyteSamplers[NonUniformResourceIndex(samplerRef.Id)], uv, dx, dy);
#else
    uint textureIndex = lumyteMap[textureRef.Id].x;
    uint samplerIndex = lumyteMap[samplerRef.Id].y;
    if (textureIndex == 0 && samplerIndex == 0)
    {
        return lumyteTexture0.SampleGrad(lumyteSampler0, uv, dx, dy);
    }

    return 0;
#endif
}
#endif
