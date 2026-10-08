[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    float2 positions[3] = { float2(-0.75, -0.75), float2(0.75, -0.75), float2(0, 0.75) };
    return float4(positions[id], 0, 1);
}

[shader("fragment")]
float4 fragmentMain() : SV_Target0
{
    return float4(1, 0, 0, 1);
}
