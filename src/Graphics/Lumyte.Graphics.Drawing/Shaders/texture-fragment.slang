#include "lumyte.slang"

struct DrawingVertex
{
    float3 Position;
    float4 Color;
    float2 TexCoord;
};
struct TextureDrawArguments
{
    GpuBufferRef<DrawingVertex> Vertices;
    float4x4 Transform;
    float4 Tint;
    GpuTextureRef Texture;
    GpuSamplerRef Sampler;
};
ConstantBuffer<TextureDrawArguments> arguments;

[shader("fragment")]
float4 main(float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target0
{
    return color * LumyteSampleGrad(arguments.Texture, arguments.Sampler, uv, ddx(uv), ddy(uv));
}
