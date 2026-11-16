#include "lumyte.slang"
struct ComputeExecutionResolveArguments2D {
    GpuBufferRef<float4> Color;
    uint Width; uint Height; uint Reserved0; uint Reserved1;
};
ConstantBuffer<ComputeExecutionResolveArguments2D> arguments;
[shader("vertex")]
float4 main(uint vertex:SV_VertexID):SV_Position {
    float2 positions[3]={float2(-1,-1),float2(3,-1),float2(-1,3)};
    return float4(positions[vertex],0,1);
}
