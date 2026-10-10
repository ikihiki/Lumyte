#include "lumyte.slang"

struct BindingStageNode
{
    uint Value;
    GpuBufferRef<BindingStageNode> Next;
    GpuBufferRef<uint> Scalars;
};

struct BindingStageArguments { GpuBufferRef<BindingStageNode> Node; };
ConstantBuffer<BindingStageArguments> arguments;

[shader("vertex")]
float4 main(uint index : SV_VertexID) : SV_Position
{
    BindingStageNode node = arguments.Node.Load(0);
    return float4(float(node.Next.Load(0).Value + node.Scalars.Load(0) + index), 0, 0, 1);
}
