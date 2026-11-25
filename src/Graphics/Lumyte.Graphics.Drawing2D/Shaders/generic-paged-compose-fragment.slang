#define GENERIC_RESOURCES 0
#include "lumyte.slang"
struct GenericResource2D { GpuTextureRef Texture; GpuSamplerRef Sampler; };
struct GenericPagedComposeArguments2D {
 GpuBufferRef<float> Coverage;
 GpuBufferRef<float4> Paint;
 GpuBufferRef<float> Clip;
 uint Width; uint Height; uint Commands; uint CommandIndex;
 float TranslationX; float TranslationY;
 uint SampleOffset; uint SampleCount; uint SceneCells; uint PageCells;
};
ConstantBuffer<GenericPagedComposeArguments2D> arguments;
#if !GENERIC_RESOURCES
#define LumyteSampleGrad(t,s,uv,dx,dy) float4(0)
#endif

[shader("fragment")]
float4 main(float4 position:SV_Position):SV_Target0 {
 uint pixel=uint(position.y)*arguments.Width+uint(position.x);
 uint first=(arguments.CommandIndex*arguments.Width*arguments.Height+pixel)*16;
 if(first<arguments.SampleOffset || first+16>arguments.SampleOffset+arguments.SampleCount) discard;
 first-=arguments.SampleOffset;
 float4 q=0;
 for(uint s=0;s<16;s++) q+=arguments.Coverage.Load(first+s)*arguments.Clip.Load(first+s)*arguments.Paint.Load(first+s);
 return q/16;
}
