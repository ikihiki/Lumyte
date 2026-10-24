#include "lumyte.slang"
struct GltfVertex { float3 Position; float3 Normal; float2 TexCoord; };
struct DrawArguments
{
    GpuBufferRef<GltfVertex> Vertices;
    float4x4 Transform;
    float4 Color;
    GpuTextureRef Texture;
    GpuSamplerRef Sampler;
};
ConstantBuffer<DrawArguments> arguments;
struct VertexOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; float lighting : TEXCOORD1; };

[shader("vertex")]
VertexOutput main(uint index : SV_VulkanVertexID)
{
    GltfVertex vertex = arguments.Vertices.Load(index);
    VertexOutput output;
    output.position = mul(float4(vertex.Position, 1), arguments.Transform);
    output.uv = vertex.TexCoord;
    output.lighting = 0.35 + 0.65 * max(0.0, dot(vertex.Normal, normalize(float3(0.5, 0.8, 1))));
    return output;
}
