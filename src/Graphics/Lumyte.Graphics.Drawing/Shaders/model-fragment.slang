#include "lumyte.slang"

struct ModelVertex
{
    float3 Position;
    float3 Normal;
    float4 Tangent;
    float2 TexCoord;
    float4 Color;
};
struct ModelTextureTransform
{
    float2 Offset;
    float2 Scale;
    float Rotation;
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
    ModelTextureTransform BaseColorUv;
    ModelTextureTransform MetallicRoughnessUv;
    ModelTextureTransform NormalUv;
    ModelTextureTransform OcclusionUv;
    ModelTextureTransform EmissiveUv;
    float EmissiveStrength;
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

float2 TransformUv(float2 uv, ModelTextureTransform transform)
{
    float2 scaled = uv * transform.Scale;
    float c = cos(transform.Rotation);
    float s = sin(transform.Rotation);
    return transform.Offset + float2(c * scaled.x - s * scaled.y, s * scaled.x + c * scaled.y);
}

[shader("fragment")]
float4 main(float3 position : TEXCOORD0, float3 normal : TEXCOORD1,
    float4 tangent : TEXCOORD2, float2 uv : TEXCOORD3,
    float4 color : COLOR0, bool front : SV_IsFrontFace) : SV_Target0
{
    float2 baseUv = TransformUv(uv, arguments.Material.BaseColorUv);
    float2 mrUv = TransformUv(uv, arguments.Material.MetallicRoughnessUv);
    float2 normalUv = TransformUv(uv, arguments.Material.NormalUv);
    float2 aoUv = TransformUv(uv, arguments.Material.OcclusionUv);
    float2 emissiveUv = TransformUv(uv, arguments.Material.EmissiveUv);
    float2 baseDx = ddx(baseUv), baseDy = ddy(baseUv);
    float2 mrDx = ddx(mrUv), mrDy = ddy(mrUv);
    float2 normalDx = ddx(normalUv), normalDy = ddy(normalUv);
    float2 aoDx = ddx(aoUv), aoDy = ddy(aoUv);
    float2 emissiveDx = ddx(emissiveUv), emissiveDy = ddy(emissiveUv);
    float3 positionDx = ddx(position), positionDy = ddy(position);
    float4 base = arguments.Material.BaseColor * color *
        LumyteSampleGrad(arguments.BaseColorTexture, arguments.BaseColorSampler, baseUv, baseDx, baseDy);
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
        // Rebuild the tangent frame when the normal texture changes its UV axes.
        if (any(arguments.Material.NormalUv.Scale != float2(1)) || arguments.Material.NormalUv.Rotation != 0)
        {
            float determinant = normalDx.x * normalDy.y - normalDx.y * normalDy.x;
            if (abs(determinant) > 1e-8)
            {
                float3 uvTangent = (positionDx * normalDy.y - positionDy * normalDx.y) / determinant;
                float3 uvBitangent = (positionDy * normalDx.x - positionDx * normalDy.x) / determinant;
                t = SafeNormalize(uvTangent - n * dot(n, uvTangent));
                b = cross(n, t) * (dot(cross(n, t), uvBitangent) < 0 ? -1 : 1);
            }
        }
        float3 mapped = LumyteSampleGrad(arguments.NormalTexture, arguments.NormalSampler, normalUv, normalDx, normalDy).xyz * 2 - 1;
        mapped.xy *= arguments.Material.NormalScale;
        n = SafeNormalize(t * mapped.x + b * mapped.y + n * mapped.z);
        n *= front ? 1 : -1;
        float3 v = SafeNormalize(arguments.CameraPosition - position);
        float3 l = SafeNormalize(arguments.LightDirection);
        float3 h = SafeNormalize(l + v);
        float4 mr = LumyteSampleGrad(arguments.MetallicRoughnessTexture, arguments.MetallicRoughnessSampler, mrUv, mrDx, mrDy);
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
        float occlusion = lerp(1, LumyteSampleGrad(arguments.OcclusionTexture, arguments.OcclusionSampler, aoUv, aoDx, aoDy).r,
            arguments.Material.OcclusionStrength);
        float3 emissive = arguments.Material.Emissive * arguments.Material.EmissiveStrength *
            LumyteSampleGrad(arguments.EmissiveTexture, arguments.EmissiveSampler, emissiveUv, emissiveDx, emissiveDy).rgb;
        result = (diffuse + distribution * visibility * fresnel) * arguments.LightColor * nl +
            base.rgb * (1 - metallic) * arguments.AmbientColor * occlusion + emissive;
    }
    float fog = 1 - exp(-max(arguments.FogDensity, 0) * length(arguments.CameraPosition - position));
    result = lerp(result, arguments.FogColor, saturate(fog));
    return float4(result, arguments.Material.AlphaMode == 2 ? base.a : 1);
}
