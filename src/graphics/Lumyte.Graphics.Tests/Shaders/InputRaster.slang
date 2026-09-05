struct RootParameters { uint4 value; float4 color; float4 padding; float4 tail; };
#if LUMYTE_SHADER_TARGET_WGSL
ConstantBuffer<RootParameters> root : register(b0, space6);
#else
[[vk::push_constant]] ConstantBuffer<RootParameters> root : register(b0, space0);
#endif
ByteAddressBuffer parameterData : register(t0, space2);
struct Output { float4 position : SV_Position; };
[shader("vertex")]
Output vertexMain(uint index : SV_VertexID)
{
    float2 positions[3] = { float2(-1,-1), float2(-1,3), float2(3,-1) };
    Output result;
    float4 transform = parameterData.Load<float4>(root.value.x * 16);
    result.position = float4(positions[index] * transform.xy + transform.zw, 0, 1);
    return result;
}
[shader("fragment")]
float4 pixelMain(Output input) : SV_Target0 { return root.color + root.tail; }
