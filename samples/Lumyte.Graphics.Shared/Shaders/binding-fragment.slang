#include "lumyte.slang"

struct BindingMaterial
{
    float4 Color;
    GpuTextureRef Texture;
    GpuSamplerRef Sampler;
};

struct BindingDrawArguments
{
    GpuBufferRef<BindingMaterial> Materials;
    uint Count;
};

ConstantBuffer<BindingDrawArguments> arguments;

[shader("fragment")]
float4 main(float4 position : SV_Position) : SV_Target0
{
    uint index = min(uint(position.x) / 4, arguments.Count - 1);
    BindingMaterial material = arguments.Materials.Load(index);
    return material.Color * LumyteSampleGrad(material.Texture, material.Sampler, float2(0.5, 0.5), float2(0, 0), float2(0, 0));
}
