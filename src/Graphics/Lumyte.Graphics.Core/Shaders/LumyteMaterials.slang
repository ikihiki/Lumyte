// Fixed library ABI: 32-byte material records and four finite sampled pairs.
struct MaterialWire
{
    float4 baseColor;
    uint textureSelector;
    uint hasTexture;
    uint padding0;
    uint padding1;
};
[[vk::binding(0, 0)]] StructuredBuffer<MaterialWire> materials;
[[vk::binding(1, 0)]] Texture2D<float4> materialTexture0;
[[vk::binding(2, 0)]] SamplerState materialSampler0;
[[vk::binding(3, 0)]] Texture2D<float4> materialTexture1;
[[vk::binding(4, 0)]] SamplerState materialSampler1;
[[vk::binding(5, 0)]] Texture2D<float4> materialTexture2;
[[vk::binding(6, 0)]] SamplerState materialSampler2;
[[vk::binding(7, 0)]] Texture2D<float4> materialTexture3;
[[vk::binding(8, 0)]] SamplerState materialSampler3;

float4 sampleMaterial(uint selector, float2 uv, float2 dx, float2 dy)
{
    switch (selector)
    {
    case 0: return materialTexture0.SampleGrad(materialSampler0, uv, dx, dy);
    case 1: return materialTexture1.SampleGrad(materialSampler1, uv, dx, dy);
    case 2: return materialTexture2.SampleGrad(materialSampler2, uv, dx, dy);
    case 3: return materialTexture3.SampleGrad(materialSampler3, uv, dx, dy);
    default: return float4(0, 0, 0, 0);
    }
}
// Material index is logical; resource selectors and bindings remain library implementation details.
float4 sampleMaterialBaseColor(uint index, float2 uv, float2 dx, float2 dy)
{
    uint count, stride;
    materials.GetDimensions(count, stride);
    if (index >= count) return float4(1, 0, 1, 1);
    MaterialWire material = materials[index];
    float4 color = material.baseColor;
    if (material.hasTexture != 0)
        color *= sampleMaterial(material.textureSelector, uv, dx, dy);
    return color;
}
