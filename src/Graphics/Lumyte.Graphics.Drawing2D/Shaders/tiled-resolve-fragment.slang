#include "lumyte.slang"
struct ComputeExecutionResolveArguments2D {
    GpuBufferRef<float4> Color;
    uint Width; uint Height; uint Reserved0; uint Reserved1;
};
ConstantBuffer<ComputeExecutionResolveArguments2D> arguments;
[shader("fragment")]
float4 main(float4 position:SV_Position):SV_Target0 {
    uint2 p=uint2(position.xy);
    if(p.x>=arguments.Width || p.y>=arguments.Height) return 0;
    // Already linear premultiplied float32. No decoding or second premultiply.
    return arguments.Color.Load(p.y*arguments.Width+p.x);
}
