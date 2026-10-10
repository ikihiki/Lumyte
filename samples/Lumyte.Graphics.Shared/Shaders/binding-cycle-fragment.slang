#include "lumyte.slang"

struct BindingStageNode
{
    uint Value;
    GpuBufferRef<BindingStageNode> Next;
    GpuBufferRef<uint> Scalars;
};

struct BindingStageArguments { GpuBufferRef<BindingStageNode> Node; };
ConstantBuffer<BindingStageArguments> arguments;

[shader("fragment")]
float4 main() : SV_Target0
{
    BindingStageNode node = arguments.Node.Load(0);
    return float4(float(node.Next.Load(0).Value + node.Scalars.Load(0)), 0, 0, 1);
}
