import LumyteBindless;
struct BufferWire
{
    uint texture;
    uint sampler;
    uint factor;
    uint reserved;
};
[[vk::binding(0, 0)]] StructuredBuffer<BufferWire> shaderData;
[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    return float4(positions[id], 0, 1);
}
[shader("fragment")]
float4 fragmentMain() : SV_Target0
{
    BufferWire data = shaderData[shaderDataIndex(0)];
    return sampleTextureGrad(data.texture, data.sampler, float2(0.5), float2(0), float2(0)) * (float(loadBufferUInt(data.factor, 0)) / 255.0);
}
