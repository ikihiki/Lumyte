import LumyteResources;

struct Bytes16 { uint4 value; };

struct Parameters
{
    [LumyteResource("View")] uint albedo;
    [LumyteResource("Sampler")] uint sampler;
    float3 tint;
    float3x3 transform;
    uint values[3];
};

struct ShaderRoot
{
    [LumyteResource("GpuAddress")] uint64_t address;
    uint scalar;
    [LumyteResource("View")] uint output;
    [LumyteResource("Sampler")] uint sampler;
    float3 colour;
};
[[vk::push_constant]] ConstantBuffer<ShaderRoot> root;

[shader("compute")][numthreads(1, 1, 1)]
void main()
{
    RWStructuredBuffer<uint> target = ResourceDescriptorHeap[root.output];
    target[0] = root.scalar + uint(root.colour.x);
}
