#include "lumyte.slang"

struct EffectArguments
{
    GpuTextureRef Input;
    GpuTextureRef Backdrop;
    GpuSamplerRef Sampler;
    float4 InputBounds;
    float4 BackdropBounds;
    float4 OutputBounds;
    float4 Settings;
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
    float4 Constant;
};

ConstantBuffer<EffectArguments> arguments;

float4 readImage(GpuTextureRef textureRef, float4 bounds, float2 pixel)
{
    float2 local = pixel - bounds.xy;
    if (any(local < 0) || any(local >= bounds.zw))
    {
        return 0;
    }

    return LumyteSampleGrad(textureRef, arguments.Sampler, local / bounds.zw, float2(0, 0), float2(0, 0));
}

[shader("fragment")]
float4 main(float4 position : SV_Position) : SV_Target0
{
    float2 pixel = floor(position.xy) + 0.5 + arguments.OutputBounds.xy;
    uint operation = uint(arguments.Settings.x);
    float4 input = readImage(arguments.Input, arguments.InputBounds, pixel);
    if (operation == 0)
    {
        float4 straight = float4(input.a > 0 ? input.rgb / input.a : float3(0), input.a);
        float4 transformed = float4(dot(arguments.Row0, straight), dot(arguments.Row1, straight), dot(arguments.Row2, straight), dot(arguments.Row3, straight)) + arguments.Constant;
        transformed.a = saturate(transformed.a);
        return float4(transformed.rgb * transformed.a, transformed.a);
    }

    if (operation == 1 || operation == 2)
    {
        float sigma = max(arguments.Settings.y, 1e-6);
        int radius = int(arguments.Settings.z);
        float4 sum = 0;
        float weightSum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            float relative = float(i) / sigma;
            float weight = exp(-0.5 * relative * relative);
            float2 offset = operation == 1 ? float2(i, 0) : float2(0, i);
            sum += readImage(arguments.Input, arguments.InputBounds, pixel + offset) * weight;
            weightSum += weight;
        }

        return sum / weightSum;
    }

    if (operation == 3)
    {
        return input * arguments.Settings.w;
    }

    if (operation == 6)
    {
        float3 color = max(input.a > 0 ? input.rgb / input.a : float3(0), 0) * exp2(arguments.Settings.y);
        float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
        color = max(lerp(float3(luminance), color, arguments.Settings.z), 0);
        float2 uv = (pixel - arguments.OutputBounds.xy) / arguments.OutputBounds.zw;
        float2 distance = (uv - 0.5) * float2(arguments.OutputBounds.z / arguments.OutputBounds.w, 1);
        color *= 1 - arguments.Settings.w * smoothstep(0.2, 0.7, length(distance));
        if (arguments.Row0.x != 0)
        {
            color = color / (1 + color);
        }
        return float4(color * input.a, input.a);
    }

    float4 backdrop = readImage(arguments.Backdrop, arguments.BackdropBounds, pixel);
    if (operation == 4)
    {
        return input + backdrop * (1 - input.a);
    }

    return float4(input.rgb + backdrop.rgb, saturate(input.a + backdrop.a));
}
