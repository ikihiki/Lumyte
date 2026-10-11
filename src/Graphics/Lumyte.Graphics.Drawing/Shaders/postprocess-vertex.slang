struct PostProcessOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
};

[shader("vertex")]
PostProcessOutput main(uint vertexId : SV_VertexID)
{
    float2 corner = float2((vertexId << 1) & 2, vertexId & 2);
    PostProcessOutput output;
    output.TexCoord = corner;
    output.Position = float4(corner * float2(2, -2) + float2(-1, 1), 0, 1);
    return output;
}
