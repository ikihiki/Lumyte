#include "lumyte.slang"
struct AdvancedDrawArguments { float4 Color; float Depth; };
ConstantBuffer<AdvancedDrawArguments> arguments;
struct VertexOutput { float4 position : SV_Position; float4 color : COLOR0; };
[shader("vertex")]
VertexOutput main(uint index : SV_VulkanVertexID)
{
    float2 positions[3] = { float2(-1, -1), float2(3, -1), float2(-1, 3) };
    VertexOutput result;
    result.position = float4(positions[index], arguments.Depth, 1);
    result.color = arguments.Color;
    return result;
}
