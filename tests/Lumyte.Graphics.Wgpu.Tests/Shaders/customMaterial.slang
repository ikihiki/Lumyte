import LumyteBindless;

// Test-owned schema: field names, ordering and stride deliberately differ from the sample.
struct CustomMaterialWire
{
    float opacity;
    uint options;
    uint image;
    uint sampler;
    float4 tint;
    float4 extra;
};
[[vk::binding(0, 0)]] StructuredBuffer<CustomMaterialWire> shaderData;
struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};
[shader("vertex")]
VertexOutput vertexMain(uint id : SV_VertexID)
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    VertexOutput output;
    output.position = float4(positions[id], 0, 1);
    output.uv = positions[id] * 0.5 + 0.5;
    return output;
}
[shader("fragment")]
float4 fragmentMain(VertexOutput input) : SV_Target0
{
    float2 dx = ddx(input.uv);
    float2 dy = ddy(input.uv);
    uint index = uint(input.uv.x * 2);
    CustomMaterialWire material = shaderData[shaderDataIndex(index)];
    return material.tint * material.opacity * sampleTextureGrad(material.image, material.sampler, input.uv, dx, dy);
}
