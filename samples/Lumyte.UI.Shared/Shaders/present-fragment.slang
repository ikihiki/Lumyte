#include "lumyte.slang"

struct UiPresentArguments
{
    GpuTextureRef Image;
    GpuSamplerRef Sampler;
    float2 Size;
};
ConstantBuffer<UiPresentArguments> arguments;

[shader("fragment")]
float4 main(float4 position : SV_Position) : SV_Target0
{
    float2 uv = position.xy / arguments.Size;
    float4 linear = LumyteSampleGrad(arguments.Image, arguments.Sampler, uv, float2(0, 0), float2(0, 0));
    float3 rgb = max(linear.rgb, float3(0, 0, 0));
    // The sample deliberately chooses an unorm swapchain. Encode the display-linear UI once.
    float3 encoded = lerp(12.92 * rgb, 1.055 * pow(rgb, float3(1.0 / 2.4)) - 0.055, step(float3(0.0031308), rgb));
    return float4(encoded, linear.a);
}
