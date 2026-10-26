#include "lumyte.slang"

struct DrawingVertex
{
    float3 Position;
    float4 Color;
    float2 TexCoord;
};
struct ColorDrawArguments
{
    GpuBufferRef<DrawingVertex> Vertices;
    float4x4 Transform;
    float4 Tint;
};
ConstantBuffer<ColorDrawArguments> arguments;

struct VertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

[shader("vertex")]
VertexOutput main(uint vertexId : SV_VertexID)
{
    DrawingVertex vertex = arguments.Vertices.Load(vertexId);
    VertexOutput output;
    output.Position = mul(float4(vertex.Position, 1), arguments.Transform);
    output.Color = vertex.Color * arguments.Tint;
    output.TexCoord = vertex.TexCoord;
    return output;
}
