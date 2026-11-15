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
struct ModelDrawRoot
{
    GpuBufferRef<ModelDrawArguments> Draw;
};
ConstantBuffer<ModelDrawRoot> arguments;

struct ModelOutput
{
    float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float4 Tangent : TEXCOORD2;
    float2 TexCoord : TEXCOORD3;
    float4 Color : COLOR0;
};

[shader("vertex")]
ModelOutput main(uint vertexId : SV_VertexID)
{
    ModelDrawArguments draw = arguments.Draw.Load(0);
    ModelVertex vertex = draw.Vertices.Load(vertexId);
    ModelOutput output;
    float4 world = mul(float4(vertex.Position, 1), draw.World);
    output.Position = mul(world, draw.ViewProjection);
    output.WorldPosition = world.xyz;
    output.Normal = mul(float4(vertex.Normal, 0), draw.NormalTransform).xyz;
    float3 tangent = mul(float4(vertex.Tangent.xyz, 0), draw.World).xyz;
    float orientation = determinant(float3x3(draw.World[0].xyz, draw.World[1].xyz, draw.World[2].xyz)) < 0 ? -1 : 1;
    output.Tangent = float4(tangent, vertex.Tangent.w * orientation);
    output.TexCoord = vertex.TexCoord;
    output.Color = vertex.Color;
    return output;
}
