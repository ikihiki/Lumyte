struct VertexOutput { float4 position : SV_Position; float4 color : COLOR0; };
[shader("vertex")]
VertexOutput main(uint index : SV_VertexID)
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    VertexOutput result;
    result.position = float4(positions[index], 0.5, 1);
    result.color = float4(1, 0, 0, 0.5);
    return result;
}
