struct RootParameters { uint4 value; float4 color; uint4 padding; float4 tail; };
#if LUMYTE_SHADER_TARGET_WGSL
ConstantBuffer<RootParameters> root : register(b0, space6);
#else
[[vk::push_constant]] ConstantBuffer<RootParameters> root : register(b0, space0);
#endif
ByteAddressBuffer parameterData : register(t0, space2);
[vk::image_format("rgba8")] WTexture2D<float4> target : register(u0, space3);
[shader("compute")]
[numthreads(1, 1, 1)]
void computeMain() { target.Store(uint2(root.value.x, 0), parameterData.Load<float4>(root.value.y * 16) + root.tail); }
