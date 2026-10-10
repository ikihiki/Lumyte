#include "lumyte.slang"
struct DispatchIndirectArguments { uint GroupCountX; uint GroupCountY; uint GroupCountZ; };
struct IndirectComputeArguments { GpuRWBufferRef<DispatchIndirectArguments> Command; GpuRWBufferRef<uint> Output; };
ConstantBuffer<IndirectComputeArguments> arguments;
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    DispatchIndirectArguments command;
    command.GroupCountX = 3;
    command.GroupCountY = 1;
    command.GroupCountZ = 1;
    arguments.Command.Store(0, command);
    arguments.Output.Store(0, 0);
    arguments.Output.Store(1, 0);
    arguments.Output.Store(2, 0);
}
