#include "lumyte.slang"

struct PostProcessArguments
{
    GpuTextureRef SceneTexture;
    GpuSamplerRef Sampler;
    float Exposure;
    float Saturation;
    float Vignette;
    float AspectRatio;
    uint ToneMap;
};
ConstantBuffer<PostProcessArguments> arguments;

[shader("fragment")]
float4 main(float2 uv : TEXCOORD0) : SV_Target0
{
    float4 source = LumyteSampleGrad(arguments.SceneTexture, arguments.Sampler, uv, ddx(uv), ddy(uv));
    float3 color = max(source.rgb, 0) * exp2(arguments.Exposure);
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    color = max(lerp(float3(luminance), color, max(arguments.Saturation, 0)), 0);
    float2 distance = (uv - 0.5) * float2(arguments.AspectRatio, 1);
    float vignette = 1 - saturate(arguments.Vignette) * smoothstep(0.2, 0.7, length(distance));
    color *= vignette;
    if (arguments.ToneMap != 0)
    {
        color = color / (1 + color);
    }
    return float4(color, source.a);
}
