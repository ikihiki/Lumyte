import LumyteBindless;

// The consumer owns this wire schema and its logical serializer.
struct MaterialWire
{
    float4 baseColor;
    uint textureReference;
    uint samplerReference;
    uint hasTexture;
    uint padding0;
};
[[vk::binding(0, 0)]] StructuredBuffer<MaterialWire> shaderData;

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};
[shader("vertex")]
VertexOutput vertexMain(uint id : SV_VertexID)
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    VertexOutput result;
    result.position = float4(positions[id], 0, 1);
    result.uv = positions[id] * 0.5 + 0.5;
    return result;
}
[shader("fragment")]
float4 fragmentMain(VertexOutput input) : SV_Target0
{
    // Every pixel in this single draw may choose a different GPU-resident material.
    float2 dx = ddx(input.uv);
    float2 dy = ddy(input.uv);
    uint index = uint(input.uv.x * 2);
    uint count, stride;
    count = shaderDataCount();
    if (index >= count) return float4(1, 0, 1, 1);
    MaterialWire material = shaderData[shaderDataIndex(index)];
    float4 color = material.baseColor;
    if (material.hasTexture != 0)
        color *= sampleTextureGrad(material.textureReference, material.samplerReference, input.uv, dx, dy);
    return color;
}
