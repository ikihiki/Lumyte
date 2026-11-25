#define GENERIC_RESOURCES 1
#include "lumyte.slang"
struct GenericResource2D { GpuTextureRef Texture; GpuSamplerRef Sampler; };
struct GenericFusedArguments2D {
 GpuBufferRef<float4> Scene;
 GpuBufferRef<GenericResource2D> Resources;
 uint Width; uint Height; uint Commands; uint CommandIndex;
 float TranslationX; float TranslationY;
};
ConstantBuffer<GenericFusedArguments2D> arguments;
#if !GENERIC_RESOURCES
#define LumyteSampleGrad(t,s,uv,dx,dy) float4(0)
#endif
#include "generic-evaluator.slang"
[shader("fragment")]
float4 main(float4 position:SV_Position):SV_Target0 {
 uint h=commandHeader(arguments.CommandIndex); float4 q=0;
 for(uint s=0;s<16;s++) {
 float2 p=floor(position.xy)+(float2(s%4,s/4)+0.5)/4-float2(arguments.TranslationX,arguments.TranslationY);
 // Canonical programs may omit an integer scissor when it was installed by the legacy recorder.
 // Generic endpoints draw a full-target primitive, so apply the captured outer clip explicitly.
 if(outerClip(h,p)!=0) q+=evaluate<256>(h,asuint(arguments.Scene.Load(h)).w,p,false);
 }
 return q/16;
}
