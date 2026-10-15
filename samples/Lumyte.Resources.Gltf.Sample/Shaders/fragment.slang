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

[shader("fragment")]
float4 main(VertexOutput input) : SV_Target0
{
    float4 texel = LumyteSampleGrad(arguments.Texture, arguments.Sampler, input.uv, ddx(input.uv), ddy(input.uv));
    return float4(texel.rgb * arguments.Color.rgb * input.lighting, texel.a * arguments.Color.a);
}
