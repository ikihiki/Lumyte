#ifndef LUMYTE_SHADER_ABI
#define LUMYTE_SHADER_ABI

struct GpuTextureRef { uint Id; uint Offset; uint Count; uint Reserved; };
struct GpuSamplerRef { uint Id; uint Offset; uint Count; uint Reserved; };

#ifdef LUMYTE_SPIRV
[[vk::binding(0, 0)]] Texture2D<float4> lumyteTextures[];
[[vk::binding(1, 0)]] SamplerState lumyteSamplers[];
#else
[[vk::binding(0, 0)]] Texture2D<float4> lumyteTexture0;
[[vk::binding(1, 0)]] SamplerState lumyteSampler0;
[[vk::binding(2, 0)]] ByteAddressBuffer lumyteBuffer0;
[[vk::binding(3, 0)]] RWByteAddressBuffer lumyteRWBuffer0;
[[vk::binding(9, 0)]] StructuredBuffer<uint4> lumyteMap;
#endif

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
        return lumyteBuffer0.Load<T>(Offset + index * sizeof(T));
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
        return lumyteRWBuffer0.Load<T>(Offset + index * sizeof(T));
#endif
    }

    void Store(uint index, T value)
    {
#ifdef LUMYTE_SPIRV
        uint64_t address = uint64_t(Id) | (uint64_t(Offset) << 32);
        ((T*)address)[index] = value;
#else
        lumyteRWBuffer0.Store<T>(Offset + index * sizeof(T), value);
#endif
    }
};

float4 LumyteSampleGrad(GpuTextureRef textureRef, GpuSamplerRef samplerRef, float2 uv, float2 dx, float2 dy)
{
#ifdef LUMYTE_SPIRV
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
