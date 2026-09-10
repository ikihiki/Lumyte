// Raw Native Vulkan shader input. Compile commands and ABI are documented in README.md.
struct Root
{
    uint64_t outputAddress;
    uint value;
    uint outputIndex;
    uint bufferIndex;
    uint textureIndex;
    uint samplerIndex;
    uint mode;
    uint4 padding0;
    uint4 padding1;
    uint4 tail;
};

[[vk::push_constant]] ConstantBuffer<Root> root;

[shader("compute")]
[numthreads(1, 1, 1)]
void rawMain(uint3 id : SV_DispatchThreadID)
{
    uint* output = (uint*)root.outputAddress;
    output[root.outputIndex + id.x] = root.value + root.tail.w + id.x;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void argumentsMain()
{
    uint* output = (uint*)root.outputAddress;
    output[0] = root.value;
    output[1] = 1;
    output[2] = 1;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void heapMain(uint3 id : SV_DispatchThreadID)
{
    RWStructuredBuffer<uint> output = ResourceDescriptorHeap[root.bufferIndex];
    Texture2D<float4> texture = ResourceDescriptorHeap[root.textureIndex];
    SamplerState sampler = SamplerDescriptorHeap[root.samplerIndex];
    float4 sampled = texture.SampleLevel(sampler, float2(0.5, 0.5), 0);
    output[root.outputIndex + id.x] = uint(round(sampled.r * 255)) + root.value + root.tail.w + id.x;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void emptyRootMain()
{
    RWStructuredBuffer<uint> output = ResourceDescriptorHeap[3];
    output[0] = 97;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void storageMain()
{
    RWTexture2D<float4> texture = ResourceDescriptorHeap[root.textureIndex];
    texture[uint2(0, 0)] = float4(float(root.value) / 255.0, 0.0, 1.0, 1.0);
}

[shader("compute")]
[numthreads(1, 1, 1)]
void readOnlyMain()
{
    StructuredBuffer<uint> input = ResourceDescriptorHeap[root.bufferIndex];
    uint* output = (uint*)root.outputAddress;
    output[0] = input[0] + root.value;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void groupsMain(uint3 id : SV_DispatchThreadID)
{
    uint* output = (uint*)root.outputAddress;
    output[id.x + id.y * 2 + id.z * 6] = id.x + id.y * 10 + id.z * 100;
}
