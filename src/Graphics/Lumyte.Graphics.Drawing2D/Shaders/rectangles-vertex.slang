#include "lumyte.slang"
struct SolidRectangleArguments2D { GpuBufferRef<float4> Data; float2 Size; };
ConstantBuffer<SolidRectangleArguments2D> arguments;
struct Output { float4 Position : SV_Position; nointerpolation uint Index : TEXCOORD0; };
[shader("vertex")]
Output main(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    float2 corners[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
    float4 bounds=arguments.Data.Load(instance*6);
    float2 pixel=bounds.xy+corners[vertex]*bounds.zw;
    Output output;
    output.Position=float4(pixel.x*2/arguments.Size.x-1,1-pixel.y*2/arguments.Size.y,0,1);
    output.Index=instance*6;
    return output;
}
