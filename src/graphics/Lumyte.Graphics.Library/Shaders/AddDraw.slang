struct RootData
{
    float4 row0;
    float4 row1;
    float4 row2;
    float4 row3;
};
#if LUMYTE_SHADER_TARGET_WGSL
ConstantBuffer<RootData> root : register(b0, space6);
#else
[[vk::push_constant]] ConstantBuffer<RootData> root : register(b0, space0);
#endif

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
VertexOutput vertexMain(uint vertexIndex : SV_VertexID)
{
    float2 positions[3] =
    {
        float2(-1.0, -1.0),
        float2(-1.0, 3.0),
        float2(3.0, -1.0)
    };

    VertexOutput output;
    float4 position = float4(positions[vertexIndex % 3], 0.0, 1.0);
    output.position = float4(
        dot(position, float4(root.row0.x, root.row1.x, root.row2.x, root.row3.x)),
        dot(position, float4(root.row0.y, root.row1.y, root.row2.y, root.row3.y)),
        dot(position, float4(root.row0.z, root.row1.z, root.row2.z, root.row3.z)),
        dot(position, float4(root.row0.w, root.row1.w, root.row2.w, root.row3.w)));
    output.uv = positions[vertexIndex % 3] * float2(0.5, -0.5) + 0.5;
    return output;
}

[shader("fragment")]
float4 pixelMain(VertexOutput input) : SV_Target0
{
    return float4(input.uv, 0.0, 1.0);
}
