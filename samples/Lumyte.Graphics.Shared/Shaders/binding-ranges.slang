#include "lumyte.slang"

struct BindingRangeArguments
{
    GpuBufferRef<uint> First;
    GpuBufferRef<uint> Second;
    GpuRWBufferRef<uint> Output;
};

ConstantBuffer<BindingRangeArguments> arguments;

[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    arguments.Output.Store(0, arguments.First.Load(0) + arguments.Second.Load(0));
}
