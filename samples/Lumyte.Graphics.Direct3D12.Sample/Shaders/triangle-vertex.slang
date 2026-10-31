struct VertexOutput { float4 position : SV_Position; float4 color : COLOR0; };
[shader("vertex")]
VertexOutput main(uint index : SV_VertexID)
{
    float2 positions[3] = { float2(0, 0.75), float2(0.75, -0.65), float2(-0.75, -0.65) };
    float3 colors[3] = { float3(1, 0.2, 0.25), float3(0.2, 1, 0.35), float3(0.25, 0.4, 1) };
    VertexOutput result;
    result.position = float4(positions[index], 0.5, 1);
    result.color = float4(colors[index], 1);
    return result;
}
