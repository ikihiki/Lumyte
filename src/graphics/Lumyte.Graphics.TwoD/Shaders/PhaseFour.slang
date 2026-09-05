StructuredBuffer<float4> layerData : register(t0, space2);
Texture2D<float4> layerTexture : register(t0, space0);
SamplerState layerSampler : register(s0, space1);

struct LayerVertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

float2 layerQuadCoordinate(uint vertexIndex)
{
    if (vertexIndex == 0 || vertexIndex == 3) return float2(0.0, 0.0);
    if (vertexIndex == 1) return float2(1.0, 0.0);
    if (vertexIndex == 5) return float2(0.0, 1.0);
    return float2(1.0, 1.0);
}

[shader("vertex")]
LayerVertexOutput layerVertex(uint vertexIndex : SV_VertexID)
{
    float2 uv = layerQuadCoordinate(vertexIndex);
    LayerVertexOutput output;
    output.position = float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0);
    output.uv = uv;
    return output;
}

[shader("fragment")]
float4 layerPixel(LayerVertexOutput input) : SV_Target0
{
    float4 settings = layerData[0];
    float4 tint = layerData[1];
    float4 offsetAndTargetSize = layerData[2];
    float2 uv = input.uv - offsetAndTargetSize.xy / offsetAndTargetSize.zw;
    float4 sampled = layerTexture.Sample(layerSampler, uv);
    if (settings.z > 0.5)
    {
        return tint * sampled.a * settings.x;
    }
    return float4(sampled.rgb * tint.rgb, sampled.a * tint.a) * settings.x;
}

[shader("fragment")]
float4 maskPixel(LayerVertexOutput input) : SV_Target0
{
    float alpha = layerTexture.Sample(layerSampler, input.uv).a;
    return float4(alpha, alpha, alpha, alpha);
}

[shader("fragment")]
float4 blurPixel(LayerVertexOutput input) : SV_Target0
{
    float radius = layerData[0].x;
    float2 targetSize = layerData[2].zw;
    float2 direction = layerData[3].xy * max(radius, 0.5) / targetSize;
    float4 color = layerTexture.Sample(layerSampler, input.uv) * 0.227027;
    color += layerTexture.Sample(layerSampler, input.uv + direction * 1.384615) * 0.316216;
    color += layerTexture.Sample(layerSampler, input.uv - direction * 1.384615) * 0.316216;
    color += layerTexture.Sample(layerSampler, input.uv + direction * 3.230769) * 0.070270;
    color += layerTexture.Sample(layerSampler, input.uv - direction * 3.230769) * 0.070270;
    return color;
}
