StructuredBuffer<float4> rasterData : register(t0, space2);

struct RasterVertexOutput
{
    float4 position : SV_Position;
    float2 localPosition : TEXCOORD0;
};

float2 rasterQuadCoordinate(uint vertexIndex)
{
    if (vertexIndex == 0 || vertexIndex == 3)
    {
        return float2(0.0, 0.0);
    }
    if (vertexIndex == 1)
    {
        return float2(1.0, 0.0);
    }
    if (vertexIndex == 5)
    {
        return float2(0.0, 1.0);
    }
    return float2(1.0, 1.0);
}

[shader("vertex")]
RasterVertexOutput distanceFieldRasterVertex(uint vertexIndex : SV_VertexID)
{
    float4 region = rasterData[0];
    float2 coordinate = rasterQuadCoordinate(vertexIndex);

    RasterVertexOutput output;
    output.position = float4(
        coordinate.x * 2.0 - 1.0,
        1.0 - coordinate.y * 2.0,
        0.0,
        1.0);
    output.localPosition = coordinate * region.zw;
    return output;
}

float edgeDistance(float2 point, float4 edge)
{
    float2 segment = edge.zw - edge.xy;
    float denominator = max(dot(segment, segment), 0.000001);
    float amount = saturate(dot(point - edge.xy, segment) / denominator);
    return length(point - edge.xy - segment * amount);
}

[shader("fragment")]
float4 distanceFieldRasterPixel(RasterVertexOutput input) : SV_Target0
{
    uint edgeCount = uint(rasterData[1].z);
    float distanceRange = rasterData[1].w;
    uint fillRule = uint(rasterData[2].x);
    uint encoding = uint(rasterData[2].y);
    float minimumDistance = 1000000.0;
    int winding = 0;
    bool odd = false;

    for (uint index = 0; index < edgeCount; index++)
    {
        float4 edge = rasterData[4 + index];
        minimumDistance = min(minimumDistance, edgeDistance(input.localPosition, edge));
        bool crosses = (edge.y > input.localPosition.y) != (edge.w > input.localPosition.y);
        float crossX = edge.x + (input.localPosition.y - edge.y)
            * (edge.z - edge.x) / max(abs(edge.w - edge.y), 0.000001)
            * (edge.w >= edge.y ? 1.0 : -1.0);
        if (crosses && crossX > input.localPosition.x)
        {
            odd = !odd;
            winding += edge.w > edge.y ? 1 : -1;
        }
    }

    bool inside = fillRule == 1 ? odd : winding != 0;
    float signedDistance = inside ? -minimumDistance : minimumDistance;
    float coverage = saturate(0.5 - signedDistance / max(fwidth(signedDistance), 0.0001));
    float encodedDistance = saturate(0.5 - signedDistance / (2.0 * distanceRange));
    float value = encoding == 0 ? coverage : encodedDistance;
    return float4(value, value, value, 1.0);
}
