#include "lumyte.slang"

struct ModelVertex
{
    float3 Position;
    float3 Normal;
    float4 Tangent;
    float2 TexCoord;
    float4 Color;
};
struct ModelMaterial
{
    float4 BaseColor;
    float3 Emissive;
    float Metallic;
    float Roughness;
    float NormalScale;
    float OcclusionStrength;
    float AlphaCutoff;
    uint AlphaMode;
    uint Unlit;
};
struct ModelDrawArguments
{
    GpuBufferRef<ModelVertex> Vertices;
    float4x4 World;
    float4x4 ViewProjection;
    float4x4 NormalTransform;
    ModelMaterial Material;
    float3 CameraPosition;
    float3 LightDirection;
    float3 LightColor;
    float3 AmbientColor;
    float3 FogColor;
    float FogDensity;
    GpuTextureRef BaseColorTexture;
    GpuSamplerRef BaseColorSampler;
    GpuTextureRef MetallicRoughnessTexture;
    GpuSamplerRef MetallicRoughnessSampler;
    GpuTextureRef NormalTexture;
    GpuSamplerRef NormalSampler;
    GpuTextureRef OcclusionTexture;
    GpuSamplerRef OcclusionSampler;
    GpuTextureRef EmissiveTexture;
    GpuSamplerRef EmissiveSampler;
};
ConstantBuffer<ModelDrawArguments> arguments;

float3 SafeNormalize(float3 value)
{
    return value * rsqrt(max(dot(value, value), 1e-12));
}

[shader("fragment")]
float4 main(float3 position : TEXCOORD0, float3 normal : TEXCOORD1,
    float4 tangent : TEXCOORD2, float2 uv : TEXCOORD3,
    float4 color : COLOR0, bool front : SV_IsFrontFace) : SV_Target0
{
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    float4 base = arguments.Material.BaseColor * color *
        LumyteSampleGrad(arguments.BaseColorTexture, arguments.BaseColorSampler, uv, dx, dy);
    if (arguments.Material.AlphaMode == 1 && base.a < arguments.Material.AlphaCutoff)
    {
        discard;
    }
    float3 result = base.rgb;
    if (arguments.Material.Unlit == 0)
    {
        float3 n = SafeNormalize(normal);
        float3 t = SafeNormalize(tangent.xyz - n * dot(n, tangent.xyz));
        float3 b = cross(n, t) * tangent.w;
        float3 mapped = LumyteSampleGrad(arguments.NormalTexture, arguments.NormalSampler, uv, dx, dy).xyz * 2 - 1;
        mapped.xy *= arguments.Material.NormalScale;
        n = SafeNormalize(t * mapped.x + b * mapped.y + n * mapped.z);
        n *= front ? 1 : -1;
        float3 v = SafeNormalize(arguments.CameraPosition - position);
        float3 l = SafeNormalize(arguments.LightDirection);
        float3 h = SafeNormalize(l + v);
        float4 mr = LumyteSampleGrad(arguments.MetallicRoughnessTexture, arguments.MetallicRoughnessSampler, uv, dx, dy);
        float metallic = saturate(arguments.Material.Metallic * mr.b);
        float roughness = clamp(arguments.Material.Roughness * mr.g, 0.045, 1);
        float nl = saturate(dot(n, l));
        float nv = max(saturate(dot(n, v)), 1e-4);
        float nh = saturate(dot(n, h));
        float vh = saturate(dot(v, h));
        float a2 = pow(roughness, 4);
        float denominator = nh * nh * (a2 - 1) + 1;
        float distribution = a2 / max(3.14159265 * denominator * denominator, 1e-6);
        float visibility = 0.5 / max(nl * sqrt(nv * nv * (1 - a2) + a2) +
            nv * sqrt(nl * nl * (1 - a2) + a2), 1e-5);
        float3 f0 = lerp(float3(0.04), base.rgb, metallic);
        float3 fresnel = f0 + (1 - f0) * pow(1 - vh, 5);
        float3 diffuse = (1 - fresnel) * (1 - metallic) * base.rgb / 3.14159265;
        float occlusion = lerp(1, LumyteSampleGrad(arguments.OcclusionTexture, arguments.OcclusionSampler, uv, dx, dy).r,
            arguments.Material.OcclusionStrength);
        float3 emissive = arguments.Material.Emissive *
            LumyteSampleGrad(arguments.EmissiveTexture, arguments.EmissiveSampler, uv, dx, dy).rgb;
        result = (diffuse + distribution * visibility * fresnel) * arguments.LightColor * nl +
            base.rgb * (1 - metallic) * arguments.AmbientColor * occlusion + emissive;
    }
    float fog = 1 - exp(-max(arguments.FogDensity, 0) * length(arguments.CameraPosition - position));
    result = lerp(result, arguments.FogColor, saturate(fog));
    return float4(result, arguments.Material.AlphaMode == 2 ? base.a : 1);
}
