import LumyteBindless;

// The scene owns its wire schema. Geometry and texture selection both come from the GPU buffer.
struct SquareWire
{
    float4 rectangle;
    uint textureReference;
    uint samplerReference;
    uint padding1;
    uint padding2;
};
[[vk::binding(0, 0)]] StructuredBuffer<SquareWire> shaderData;
struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
    nointerpolation uint materialIndex : TEXCOORD1;
};
[shader("vertex")]
VertexOutput vertexMain(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(0, 1),
        float2(0, 1), float2(1, 0), float2(1, 1)
    };
    SquareWire material = shaderData[shaderDataIndex(instance)];
    VertexOutput result;
    result.position = float4(material.rectangle.xy + corners[vertex] * material.rectangle.zw, 0, 1);
    result.uv = corners[vertex];
    result.materialIndex = instance;
    return result;
}
[shader("fragment")]
float4 fragmentMain(VertexOutput input) : SV_Target0
{
    float2 dx = ddx(input.uv);
    float2 dy = ddy(input.uv);
    SquareWire material = shaderData[shaderDataIndex(input.materialIndex)];
    return sampleTextureGrad(material.textureReference, material.samplerReference, input.uv, dx, dy);
}
