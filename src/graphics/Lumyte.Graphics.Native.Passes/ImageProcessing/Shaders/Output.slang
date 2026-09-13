// Native direct root input. The descriptor index refers to this runtime's resource heap.
struct OutputRoot
{
    uint textureIndex;
    uint encodeSrgb;
    uint premultiplied;
    uint hardwareSrgb;
};
[[vk::push_constant]] ConstantBuffer<OutputRoot> root;

[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    float2 position = float2((id << 1) & 2, id & 2);
    return float4(position * 2 - 1, 0, 1);
}

float3 encode(float3 value)
{
    return select(value <= 0.0031308, value * 12.92, 1.055 * pow(max(value, 0), 1.0 / 2.4) - 0.055);
}
float3 decode(float3 value)
{
    return select(value <= 0.04045, value / 12.92, pow((max(value, 0) + 0.055) / 1.055, 2.4));
}

[shader("fragment")]
float4 pixelMain(float4 position : SV_Position) : SV_Target0
{
    Texture2D<float4> source = ResourceDescriptorHeap[root.textureIndex];
    float4 color = source.Load(int3(int2(position.xy), 0));
    float3 straight = color.a > 0 ? color.rgb / color.a : float3(0);
    float3 encoded = root.encodeSrgb != 0 ? encode(straight) : straight;
    float alpha = root.premultiplied != 0 ? color.a : 1;
    float3 output = encoded * alpha;
    // Hardware sRGB attachments encode after this shader, including after alpha multiplication.
    return float4(root.hardwareSrgb != 0 ? decode(output) : output, alpha);
}
