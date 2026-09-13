// Native Vulkan raster fixture: no compiler Y inversion; the backend uses a negative viewport height.
struct Root
{
    uint64_t vertices;
    uint64_t arguments;
    float4 color;
    float depth;
    float instanceOffset;
    uint textureIndex;
    uint samplerIndex;
    uint4 padding;
    uint4 tail;
};
[[vk::push_constant]] ConstantBuffer<Root> root;

struct VertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
VertexOutput vertexMain(uint vertex : SV_VulkanVertexID, uint instance : SV_VulkanInstanceID)
{
    float2* positions = (float2*)root.vertices;
    float2 p = positions[vertex];
    p.x += float(instance) * root.instanceOffset;
    VertexOutput result;
    result.position = float4(p, root.depth, 1);
    return result;
}

[shader("fragment")]
float4 pixelMain() : SV_Target0
{
    return root.color + float4(float(root.tail.w) / 255, 0, 0, 0);
}

[shader("fragment")]
float4 sampledMain() : SV_Target0
{
    Texture2D<float4> image = ResourceDescriptorHeap[root.textureIndex];
    SamplerState sampler = SamplerDescriptorHeap[root.samplerIndex];
    return image.SampleLevel(sampler, float2(0.5, 0.5), 0);
}

[shader("compute")]
[numthreads(1, 1, 1)]
void drawArgumentsMain()
{
    uint* arguments = (uint*)root.arguments;
    arguments[0] = 3;
    arguments[1] = 1;
    arguments[2] = 1;
    arguments[3] = 2;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void indexedArgumentsMain()
{
    uint* arguments = (uint*)root.arguments;
    arguments[0] = 3;
    arguments[1] = 1;
    arguments[2] = 1;
    arguments[3] = asuint(-1);
    arguments[4] = 2;
}
