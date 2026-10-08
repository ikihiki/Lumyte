import LumyteBindless;
struct TextureWire
{
    uint texture0;
    uint texture1;
    uint texture2;
    uint texture3;
    uint texture4;
    uint texture5;
    uint texture6;
    uint texture7;
    uint texture8;
    uint sampler;
    uint reserved0;
    uint reserved1;
};
[[vk::binding(0, 0)]] StructuredBuffer<TextureWire> shaderData;
[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    return float4(positions[id], 0, 1);
}
[shader("fragment")]
float4 fragmentMain() : SV_Target0
{
    TextureWire data = shaderData[shaderDataIndex(0)];
    float4 result = float4(0);
    if (data.texture0 != 0) result += sampleTextureGrad(data.texture0, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture1 != 0) result += sampleTextureGrad(data.texture1, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture2 != 0) result += sampleTextureGrad(data.texture2, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture3 != 0) result += sampleTextureGrad(data.texture3, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture4 != 0) result += sampleTextureGrad(data.texture4, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture5 != 0) result += sampleTextureGrad(data.texture5, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture6 != 0) result += sampleTextureGrad(data.texture6, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture7 != 0) result += sampleTextureGrad(data.texture7, data.sampler, float2(0.5), float2(0), float2(0));
    if (data.texture8 != 0) result += sampleTextureGrad(data.texture8, data.sampler, float2(0.5), float2(0), float2(0));
    return result / 5.0;
}
