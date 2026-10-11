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
    ModelVertex vertex = arguments.Vertices.Load(vertexId);
    ModelOutput output;
    float4 world = mul(float4(vertex.Position, 1), arguments.World);
    output.Position = mul(world, arguments.ViewProjection);
    output.WorldPosition = world.xyz;
    output.Normal = mul(float4(vertex.Normal, 0), arguments.NormalTransform).xyz;
    float3 tangent = mul(float4(vertex.Tangent.xyz, 0), arguments.World).xyz;
    float orientation = determinant(float3x3(arguments.World[0].xyz, arguments.World[1].xyz, arguments.World[2].xyz)) < 0 ? -1 : 1;
    output.Tangent = float4(tangent, vertex.Tangent.w * orientation);
    output.TexCoord = vertex.TexCoord;
    output.Color = vertex.Color;
    return output;
}
