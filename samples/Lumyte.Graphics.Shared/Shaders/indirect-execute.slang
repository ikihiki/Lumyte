#include "lumyte.slang"
struct IndirectExecuteArguments { GpuRWBufferRef<uint> Output; };
ConstantBuffer<IndirectExecuteArguments> arguments;
[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 group : SV_GroupID)
{
    arguments.Output.Store(group.x, (group.x + 1) * 10);
}
