StructuredBuffer<float4> layerData : register(t0, space2);
#if LUMYTE_SHADER_TARGET_WGSL
Texture2D<float4> layerTexture : register(t0, space0);
Texture2D<float4> backdropTexture : register(t1, space0);
Texture2D<float4> coverageTexture : register(t2, space0);
SamplerState layerSampler : register(s0, space1);
#else
Texture2D<float4> layerTextures[3] : register(t0, space0);
SamplerState layerSamplers[1] : register(s0, space1);
#endif

float4 sampleLayer(float2 uv)
{
#if LUMYTE_SHADER_TARGET_WGSL
    return layerTexture.Sample(layerSampler, uv);
#else
    return layerTextures[0].Sample(layerSamplers[0], uv);
#endif
}

float4 sampleBackdrop(float2 uv)
{
#if LUMYTE_SHADER_TARGET_WGSL
    return backdropTexture.Sample(layerSampler, uv);
#else
    return layerTextures[1].Sample(layerSamplers[0], uv);
#endif
}

float sampleCoverage(float2 uv)
{
#if LUMYTE_SHADER_TARGET_WGSL
    return coverageTexture.Sample(layerSampler, uv).a;
#else
    return layerTextures[2].Sample(layerSamplers[0], uv).a;
#endif
}

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
    float4 sampled = sampleLayer(uv);
    if (settings.z > 0.5)
    {
        return tint * sampled.a * settings.x;
    }
    return float4(sampled.rgb * tint.rgb, sampled.a * tint.a) * settings.x;
}

float4 adjustedLayerColor(float2 uv)
{
    float4 settings = layerData[0];
    float4 tint = layerData[1];
    float4 offsetAndTargetSize = layerData[2];
    float2 sourceUv = uv - offsetAndTargetSize.xy / offsetAndTargetSize.zw;
    float4 sampled = sampleLayer(sourceUv);
    if (settings.z > 0.5)
    {
        return tint * sampled.a * settings.x;
    }
    return float4(sampled.rgb * tint.rgb, sampled.a * tint.a) * settings.x;
}

float softLightFunction(float backdrop)
{
    if (backdrop <= 0.25)
    {
        return ((16.0 * backdrop - 12.0) * backdrop + 4.0) * backdrop;
    }
    return sqrt(backdrop);
}

float blendComponent(int mode, float backdrop, float source)
{
    if (mode == 14)
    {
        return backdrop <= 0.5
            ? 2.0 * backdrop * source
            : 1.0 - 2.0 * (1.0 - backdrop) * (1.0 - source);
    }
    if (mode == 15) return min(backdrop, source);
    if (mode == 16) return max(backdrop, source);
    if (mode == 17)
    {
        if (backdrop <= 0.0) return 0.0;
        return source >= 1.0 ? 1.0 : min(1.0, backdrop / (1.0 - source));
    }
    if (mode == 18)
    {
        if (backdrop >= 1.0) return 1.0;
        return source <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - backdrop) / source);
    }
    if (mode == 19)
    {
        return source <= 0.5
            ? 2.0 * backdrop * source
            : 1.0 - 2.0 * (1.0 - backdrop) * (1.0 - source);
    }
    if (mode == 20)
    {
        return source <= 0.5
            ? backdrop - (1.0 - 2.0 * source) * backdrop * (1.0 - backdrop)
            : backdrop + (2.0 * source - 1.0) * (softLightFunction(backdrop) - backdrop);
    }
    if (mode == 21) return abs(backdrop - source);
    if (mode == 22) return backdrop + source - 2.0 * backdrop * source;
    return backdrop * source;
}

float colorLuminosity(float3 color)
{
    return dot(color, float3(0.3, 0.59, 0.11));
}

float colorSaturation(float3 color)
{
    return max(color.r, max(color.g, color.b)) - min(color.r, min(color.g, color.b));
}

float3 clipColor(float3 color)
{
    float luminosity = colorLuminosity(color);
    float minimum = min(color.r, min(color.g, color.b));
    float maximum = max(color.r, max(color.g, color.b));
    if (minimum < 0.0)
    {
        color = luminosity + (color - luminosity) * luminosity / (luminosity - minimum);
    }
    if (maximum > 1.0)
    {
        color = luminosity
            + (color - luminosity) * (1.0 - luminosity) / (maximum - luminosity);
    }
    return color;
}

float3 setLuminosity(float3 color, float luminosity)
{
    return clipColor(color + luminosity - colorLuminosity(color));
}

float3 setSaturation(float3 color, float saturation)
{
    float minimum = min(color.r, min(color.g, color.b));
    float maximum = max(color.r, max(color.g, color.b));
    float range = maximum - minimum;
    return range > 0.0 ? (color - minimum) * saturation / range : float3(0.0);
}

float3 blendColor(int mode, float3 backdrop, float3 source)
{
    if (mode <= 23)
    {
        return float3(
            blendComponent(mode, backdrop.r, source.r),
            blendComponent(mode, backdrop.g, source.g),
            blendComponent(mode, backdrop.b, source.b));
    }
    if (mode == 24)
    {
        return setLuminosity(
            setSaturation(source, colorSaturation(backdrop)),
            colorLuminosity(backdrop));
    }
    if (mode == 25)
    {
        return setLuminosity(
            setSaturation(backdrop, colorSaturation(source)),
            colorLuminosity(backdrop));
    }
    if (mode == 26) return setLuminosity(source, colorLuminosity(backdrop));
    return setLuminosity(backdrop, colorLuminosity(source));
}

float4 compositePremultiplied(int mode, float4 source, float4 backdrop)
{
    float sourceAlpha = source.a;
    float backdropAlpha = backdrop.a;
    if (mode == 0) return float4(0.0);
    if (mode == 1) return source;
    if (mode == 2) return backdrop;
    if (mode == 3) return source + backdrop * (1.0 - sourceAlpha);
    if (mode == 4) return source * (1.0 - backdropAlpha) + backdrop;
    if (mode == 5) return source * backdropAlpha;
    if (mode == 6) return backdrop * sourceAlpha;
    if (mode == 7) return source * (1.0 - backdropAlpha);
    if (mode == 8) return backdrop * (1.0 - sourceAlpha);
    if (mode == 9)
    {
        return source * backdropAlpha + backdrop * (1.0 - sourceAlpha);
    }
    if (mode == 10)
    {
        return source * (1.0 - backdropAlpha) + backdrop * sourceAlpha;
    }
    if (mode == 11)
    {
        return source * (1.0 - backdropAlpha) + backdrop * (1.0 - sourceAlpha);
    }
    if (mode == 12) return min(float4(1.0), source + backdrop);

    float alpha = sourceAlpha + backdropAlpha - sourceAlpha * backdropAlpha;
    if (mode == 13)
    {
        return float4(source.rgb + backdrop.rgb - source.rgb * backdrop.rgb, alpha);
    }

    float3 sourceColor = sourceAlpha > 0.0 ? source.rgb / sourceAlpha : float3(0.0);
    float3 backdropColor = backdropAlpha > 0.0 ? backdrop.rgb / backdropAlpha : float3(0.0);
    float3 blended = blendColor(mode, backdropColor, sourceColor);
    float3 result = source.rgb * (1.0 - backdropAlpha)
        + backdrop.rgb * (1.0 - sourceAlpha)
        + blended * (sourceAlpha * backdropAlpha);
    return float4(result, alpha);
}

[shader("fragment")]
float4 compositePixel(LayerVertexOutput input) : SV_Target0
{
    float4 source = adjustedLayerColor(input.uv);
    float4 backdrop = sampleBackdrop(input.uv);
    int mode = (int)round(layerData[0].w);
    float4 composed = compositePremultiplied(mode, source, backdrop);
    float coverage = layerData[3].z > 0.5 ? saturate(sampleCoverage(input.uv)) : 1.0;
    return lerp(backdrop, composed, coverage);
}

[shader("fragment")]
float4 clippedLayerPixel(LayerVertexOutput input) : SV_Target0
{
    // Keep the same three-texture ABI as compositePixel on WebGPU as well as
    // descriptor-array backends. Direction.w is reserved and written as zero.
    return adjustedLayerColor(input.uv) * saturate(sampleCoverage(input.uv))
        + sampleBackdrop(input.uv) * layerData[3].w;
}

[shader("fragment")]
float4 copyPixel(LayerVertexOutput input) : SV_Target0
{
    return sampleLayer(input.uv);
}

[shader("fragment")]
float4 maskPixel(LayerVertexOutput input) : SV_Target0
{
    float alpha = sampleLayer(input.uv).a;
    return float4(alpha, alpha, alpha, alpha);
}

[shader("fragment")]
float4 blurPixel(LayerVertexOutput input) : SV_Target0
{
    float radius = layerData[0].x;
    float2 targetSize = layerData[2].zw;
    float2 direction = layerData[3].xy * max(radius, 0.5) / targetSize;
    float4 color = sampleLayer(input.uv) * 0.227027;
    color += sampleLayer(input.uv + direction * 1.384615) * 0.316216;
    color += sampleLayer(input.uv - direction * 1.384615) * 0.316216;
    color += sampleLayer(input.uv + direction * 3.230769) * 0.070270;
    color += sampleLayer(input.uv - direction * 3.230769) * 0.070270;
    return color;
}
