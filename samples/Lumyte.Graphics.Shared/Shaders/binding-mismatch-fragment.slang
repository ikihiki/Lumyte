#include "lumyte.slang"

struct BindingMismatchMaterial { float4 Color; uint Id; };
struct BindingMismatchNode { GpuBufferRef<BindingMismatchMaterial> Material; };
struct BindingMismatchArguments { GpuBufferRef<BindingMismatchNode> Node; };
ConstantBuffer<BindingMismatchArguments> arguments;

[shader("fragment")]
float4 main() : SV_Target0
{
    BindingMismatchNode node = arguments.Node.Load(0);
    BindingMismatchMaterial material = node.Material.Load(0);
    return material.Color + float4(float(material.Id), 0, 0, 0);
}
