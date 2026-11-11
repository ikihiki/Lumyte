#include "lumyte.slang"

struct BindingNode
{
    uint Value;
    GpuBufferRef<BindingNode> Next;
};

struct BindingVector
{
    float3 Value;
};

struct BindingMatrixData
{
    float4x4 Transform;
    BindingVector Child;
    GpuBufferRef<BindingNode> Node;
};

struct BindingMatrixArguments
{
    GpuBufferRef<BindingMatrixData> Data;
    GpuRWBufferRef<uint> Output;
};

ConstantBuffer<BindingMatrixArguments> arguments;

[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    BindingMatrixData data = arguments.Data.Load(0);
    float4 transformed = mul(float4(1, 2, 3, 1), data.Transform);
    arguments.Output.Store(0, uint(transformed.x));
    arguments.Output.Store(1, uint(transformed.y));
    arguments.Output.Store(2, uint(transformed.z));
    arguments.Output.Store(3, uint(transformed.w));
    BindingNode node = data.Node.Load(0);
    arguments.Output.Store(4, node.Value + uint(data.Child.Value.x + data.Child.Value.y + data.Child.Value.z));
    arguments.Output.Store(5, node.Next.Load(0).Value);
}
