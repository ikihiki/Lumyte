#include "lumyte.slang"
struct NumericBitsArguments { GpuBufferRef<float4> Data; GpuRWBufferRef<uint> Output; };
ConstantBuffer<NumericBitsArguments> arguments;
[shader("compute")]
[numthreads(1,1,1)]
void main(uint3 thread:SV_DispatchThreadID)
{
    if(thread.x>=16) return;
    float4 cell=arguments.Data.Load(min(thread.x/4,arguments.Data.Count-1));
    uint4 bits=asuint(cell);
    arguments.Output.Store(min(thread.x,arguments.Output.Count-1),bits[thread.x%4]);
}
