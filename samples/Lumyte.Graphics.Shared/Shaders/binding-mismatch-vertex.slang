#include "lumyte.slang"

struct BindingMismatchMaterial { uint Id; float4 Color; };
struct BindingMismatchNode { GpuBufferRef<BindingMismatchMaterial> Material; };
struct BindingMismatchArguments { GpuBufferRef<BindingMismatchNode> Node; };
ConstantBuffer<BindingMismatchArguments> arguments;

[shader("vertex")]
float4 main(uint index : SV_VertexID) : SV_Position
{
    BindingMismatchNode node = arguments.Node.Load(0);
    BindingMismatchMaterial material = node.Material.Load(0);
    return material.Color + float4(float(index + material.Id), 0, 0, 0);
}
