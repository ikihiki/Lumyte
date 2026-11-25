#define GENERIC_RESOURCES 0
#include "lumyte.slang"
struct GenericResource2D { GpuTextureRef Texture; GpuSamplerRef Sampler; };
struct GenericCoverageArguments2D {
 GpuBufferRef<float4> Scene;
 GpuRWBufferRef<float> Coverage;
 uint Width; uint Height; uint Commands; uint CommandIndex;
 float TranslationX; float TranslationY;
};
ConstantBuffer<GenericCoverageArguments2D> arguments;
#if !GENERIC_RESOURCES
#define LumyteSampleGrad(t,s,uv,dx,dy) float4(0)
#endif
#include "generic-evaluator.slang"
[shader("compute")]
[numthreads(64,1,1)]
void main(uint3 id:SV_DispatchThreadID) {
 uint index=id.x+id.y*arguments.CommandIndex;
 uint total=arguments.Commands*arguments.Width*arguments.Height*16;
 if(index>=total) return;
 uint s=index%16,pixel=(index/16)%(arguments.Width*arguments.Height),j=index/(16*arguments.Width*arguments.Height);
 uint h=commandHeader(j);
 float2 p=float2(pixel%arguments.Width,pixel/arguments.Width)+(float2(s%4,s/4)+0.5)/4-float2(arguments.TranslationX,arguments.TranslationY);
 arguments.Coverage.Store(index,evaluate<256>(h,asuint(arguments.Scene.Load(h+5)).x,p,true).x);
}
