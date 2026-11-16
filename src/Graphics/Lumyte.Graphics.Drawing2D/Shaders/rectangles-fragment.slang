#include "lumyte.slang"
struct SolidRectangleArguments2D { GpuBufferRef<float4> Data; float2 Size; };
ConstantBuffer<SolidRectangleArguments2D> arguments;
float2 localPoint(float2 pixel,uint offset)
{
    float4 t0=arguments.Data.Load(offset+1),t1=arguments.Data.Load(offset+2);
    return float2(dot(t0.xyz,float3(pixel,1)),dot(t1.xyz,float3(pixel,1)));
}
bool inside(float2 pixel,uint offset)
{
    float4 bounds=arguments.Data.Load(offset);
    float2 p=localPoint(pixel,offset);
    return all(p>=bounds.xy) && all(p<bounds.xy+bounds.zw);
}
bool fullyInside(float2 origin,uint offset)
{
    float4 bounds=arguments.Data.Load(offset);
    float2 a=localPoint(origin+0.125,offset),b=localPoint(origin+0.875,offset);
    return all(min(a,b)>=bounds.xy) && all(max(a,b)<bounds.xy+bounds.zw);
}
bool normalizedClip(float2 pixel,float4 clip)
{
    if(clip.y==0 || clip.y==2) return true;
    if(clip.y==1) return false;
    uint descriptor=min(uint(clip.x),arguments.Data.Count-1);
    float4 bounds=arguments.Data.Load(descriptor);
    if(clip.y==3)
    {
        float2 sampleIndex=floor(pixel*4);
        return all(sampleIndex>=bounds.xy) && all(sampleIndex<bounds.zw);
    }
    int2 point=int2(floor(pixel))-int2(bounds.xy);
    if(any(point<0) || any(point>=int2(bounds.zw))) return false;
    uint index=uint(point.y)*uint(bounds.z)+uint(point.x);
    uint first=uint(arguments.Data.Load(min(descriptor+1,arguments.Data.Count-1)).x);
    float4 packed=arguments.Data.Load(min(first+index/4,arguments.Data.Count-1));
    uint bits=uint(packed[index%4]);
    uint2 sub=uint2(floor(frac(pixel)*4));
    return ((bits>>(sub.y*4+sub.x))&1)!=0;
}
[shader("fragment")]
float4 main(float4 position : SV_Position,nointerpolation uint index : TEXCOORD0) : SV_Target0
{
    float4 color=arguments.Data.Load(index+4),clips=arguments.Data.Load(index+5);
    float4 result=0;
    float2 origin=floor(position.xy);
    if(clips.z!=0 && (clips.y==0 || clips.y==2))
    {
        bool full=fullyInside(origin,index+1);
        if(full)
        {
            // Preserve the same sample accumulation, while avoiding repeated
            // buffer loads and transforms for pixels whose 16 samples are inside.
            for(uint sample=0;sample<16;sample++) result+=color;
            return result/16;
        }
    }
    for(uint y=0;y<4;y++)
    for(uint x=0;x<4;x++)
    {
        float2 p=origin+(float2(x,y)+0.5)/4;
        bool covered=inside(p,index+1);
        covered=covered && normalizedClip(p,clips);
        result+=covered ? color : float4(0);
    }
    return result/16;
}
