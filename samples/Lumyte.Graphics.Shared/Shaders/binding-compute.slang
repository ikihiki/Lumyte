#include "lumyte.slang"

struct BindingNode
{
    uint Value;
    GpuBufferRef<BindingNode> Next;
};

struct BindingComputeArguments
{
    float4x4 Camera;
    uint Factor;
    GpuBufferRef<BindingNode> Node;
    GpuRWBufferRef<uint> Output;
};

ConstantBuffer<BindingComputeArguments> arguments;

[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    BindingNode a = arguments.Node.Load(0);
    BindingNode b = a.Next.Load(0);
    BindingNode c = b.Next.Load(0);
    arguments.Output.Store(0, a.Value + b.Value + c.Value + arguments.Factor + uint(arguments.Camera[0][0]));
}
