#include "lumyte.slang"

struct BindingVector { float3 Value; };
struct BindingPackedVector { uint Id; float3 Color; };
struct BindingNestedVector { BindingVector Child; uint Tail; };

struct BindingLayoutArguments
{
    GpuBufferRef<BindingVector> Vectors;
    GpuBufferRef<BindingPackedVector> Packed;
    GpuBufferRef<BindingNestedVector> Nested;
    GpuRWBufferRef<uint> Output;
};

ConstantBuffer<BindingLayoutArguments> arguments;

[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    for (uint i = 0; i < 2; i++)
    {
        BindingVector vectorValue = arguments.Vectors.Load(i);
        BindingPackedVector packed = arguments.Packed.Load(i);
        BindingNestedVector nested = arguments.Nested.Load(i);
        arguments.Output.Store(i * 3, uint(vectorValue.Value.x));
        arguments.Output.Store(i * 3 + 1, uint(vectorValue.Value.y));
        arguments.Output.Store(i * 3 + 2, uint(vectorValue.Value.z));
        arguments.Output.Store(6 + i * 4, packed.Id);
        arguments.Output.Store(7 + i * 4, uint(packed.Color.x));
        arguments.Output.Store(8 + i * 4, uint(packed.Color.y));
        arguments.Output.Store(9 + i * 4, uint(packed.Color.z));
        arguments.Output.Store(14 + i * 4, uint(nested.Child.Value.x));
        arguments.Output.Store(15 + i * 4, uint(nested.Child.Value.y));
        arguments.Output.Store(16 + i * 4, uint(nested.Child.Value.z));
        arguments.Output.Store(17 + i * 4, nested.Tail);
    }
}
