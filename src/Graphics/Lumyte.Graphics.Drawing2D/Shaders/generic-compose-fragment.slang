#define GENERIC_RESOURCES 0
#include "lumyte.slang"
struct GenericResource2D { GpuTextureRef Texture; GpuSamplerRef Sampler; };
struct GenericComposeArguments2D {
 GpuBufferRef<float4> Scene;
 GpuBufferRef<float> Coverage;
 GpuBufferRef<float4> Paint;
 GpuBufferRef<float> Clip;
 uint Width; uint Height; uint Commands; uint CommandIndex;
};
ConstantBuffer<GenericComposeArguments2D> arguments;
#if !GENERIC_RESOURCES
#define LumyteSampleGrad(t,s,uv,dx,dy) float4(0)
#endif
#include "generic-evaluator.slang"
[shader("fragment")]
float4 main(float4 position:SV_Position):SV_Target0 {
 uint pixel=uint(position.y)*arguments.Width+uint(position.x);
 uint first=(arguments.CommandIndex*arguments.Width*arguments.Height+pixel)*16;
 float4 q=0;
 for(uint s=0;s<16;s++) q+=arguments.Coverage.Load(first+s)*arguments.Clip.Load(first+s)*arguments.Paint.Load(first+s);
 return q/16;
}
