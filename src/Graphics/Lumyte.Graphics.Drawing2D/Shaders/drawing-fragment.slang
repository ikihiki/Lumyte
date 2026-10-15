#include "lumyte.slang"

struct DrawingInstruction
{
    float4 T0; float4 T1; float4 A; float4 B; float4 C; float4 D;
    uint Kind; uint First; uint Count; uint Mode;
    GpuTextureRef Texture; GpuSamplerRef Sampler;
};
struct DrawingArguments
{
    GpuBufferRef<DrawingInstruction> Instructions;
    GpuBufferRef<float4> Data;
    uint Count; uint Root; uint GraphMode; float Opacity;
    float4 SourceBounds; float4 OriginalBounds; float4 PixelOrigin;
    GpuTextureRef SourceTexture; GpuTextureRef OriginalTexture; GpuSamplerRef GraphSampler;
};
ConstantBuffer<DrawingArguments> arguments;

float cross2(float2 a, float2 b) { return a.x * b.y - a.y * b.x; }
float lum(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }
float sat(float3 c) { return max(c.x, max(c.y,c.z)) - min(c.x,min(c.y,c.z)); }
float3 clipColor(float3 c)
{
    float l = lum(c), n = min(c.x,min(c.y,c.z)), x = max(c.x,max(c.y,c.z));
    if(n < 0) c = l + ((c-l)*l / max(l-n, 1e-20));
    if(x > 1) c = l + ((c-l)*(1-l) / max(x-l, 1e-20));
    return c;
}
float3 setLum(float3 c, float l) { return clipColor(c + l-lum(c)); }
float3 setSat(float3 c, float s)
{
    float low = min(c.x,min(c.y,c.z)), high = max(c.x,max(c.y,c.z));
    return high > low ? (c-low)*s/(high-low) : float3(0);
}
float blendChannel(float s, float b, uint mode)
{
    if(mode == 13) return s+b-s*b;
    if(mode == 14) return b <= 0.5 ? 2*s*b : 1-2*(1-s)*(1-b);
    if(mode == 15) return min(s,b);
    if(mode == 16) return max(s,b);
    if(mode == 17) return b == 0 ? 0 : s >= 1 ? 1 : min(1,b/(1-s));
    if(mode == 18) return b >= 1 ? 1 : s == 0 ? 0 : 1-min(1,(1-b)/s);
    if(mode == 19) return s <= 0.5 ? 2*s*b : 1-2*(1-s)*(1-b);
    if(mode == 20)
    {
        float d = b <= 0.25 ? ((16*b-12)*b+4)*b : sqrt(max(b,0));
        return s <= 0.5 ? b-(1-2*s)*b*(1-b) : b+(2*s-1)*(d-b);
    }
    if(mode == 21) return abs(b-s);
    if(mode == 22) return b+s-2*b*s;
    return b*s;
}
float4 composite(float4 s, float4 b, uint mode)
{
    if(mode == 0) return 0;
    if(mode == 1) return s;
    if(mode == 2) return b;
    if(mode == 3) return s+b*(1-s.a);
    if(mode == 4) return b+s*(1-b.a);
    if(mode == 5) return s*b.a;
    if(mode == 6) return b*s.a;
    if(mode == 7) return s*(1-b.a);
    if(mode == 8) return b*(1-s.a);
    if(mode == 9) return s*b.a+b*(1-s.a);
    if(mode == 10) return b*s.a+s*(1-b.a);
    if(mode == 11) return s*(1-b.a)+b*(1-s.a);
    if(mode == 12) return min(s+b,1);
    float3 cs = s.a > 0 ? s.rgb/s.a : float3(0);
    float3 cb = b.a > 0 ? b.rgb/b.a : float3(0);
    float3 mixed;
    if(mode == 24) mixed = setLum(setSat(cs,sat(cb)),lum(cb));
    else if(mode == 25) mixed = setLum(setSat(cb,sat(cs)),lum(cb));
    else if(mode == 26) mixed = setLum(cs,lum(cb));
    else if(mode == 27) mixed = setLum(cb,lum(cs));
    else mixed = float3(blendChannel(cs.r,cb.r,mode),blendChannel(cs.g,cb.g,mode),blendChannel(cs.b,cb.b,mode));
    return float4(s.rgb*(1-b.a)+b.rgb*(1-s.a)+s.a*b.a*mixed,s.a+b.a-s.a*b.a);
}
float4 colorLine(DrawingInstruction n, float t)
{
    if(n.Count == 0) return 0;
    float start = arguments.Data.Load(n.First).x;
    float end = arguments.Data.Load(n.First+(n.Count-1)*2).x;
    float range = end-start;
    if(range > 0 && n.Mode != 0)
    {
        float u = (t-start)/range;
        if(n.Mode == 1) u = u-floor(u);
        else { u = u-2*floor(u/2); u = u <= 1 ? u : 2-u; }
        t = start+u*range;
    }
    if(t < start) return arguments.Data.Load(n.First+1);
    uint left = 0;
    for(uint i=1; i<n.Count; i++)
    {
        float offset = arguments.Data.Load(n.First+i*2).x;
        if(t < offset)
        {
            float before = arguments.Data.Load(n.First+left*2).x;
            return lerp(arguments.Data.Load(n.First+left*2+1), arguments.Data.Load(n.First+i*2+1), (t-before)/(offset-before));
        }
        left=i;
    }
    return arguments.Data.Load(n.First+(n.Count-1)*2+1);
}
int tiled(int index, int extent, uint mode)
{
    if(mode==0) return clamp(index,0,extent-1);
    if(mode==3) return index<0 || index>=extent ? -1 : index;
    int period = mode==1 ? extent : extent*2;
    int i = ((index%period)+period)%period;
    return mode==2 && i>=extent ? period-1-i : i;
}
float4 imageTap(DrawingInstruction n, int2 pixel)
{
    int x=tiled(pixel.x,int(n.A.z),uint(n.B.z));
    int y=tiled(pixel.y,int(n.A.w),uint(n.B.w));
    if(x<0 || y<0) return 0;
    float2 uv=(n.A.xy+float2(x+0.5,y+0.5))/n.B.xy;
    float4 color=LumyteSampleGrad(n.Texture,n.Sampler,uv,0,0);
    if(n.C.y==0) color.rgb*=color.a;
    return color;
}
float4 imagePattern(DrawingInstruction n,float2 p)
{
    if(n.C.x==0) return imageTap(n,int2(floor(p)));
    float2 point=p-0.5, fraction=frac(point);
    int2 corner=int2(floor(point));
    return lerp(lerp(imageTap(n,corner),imageTap(n,corner+int2(1,0)),fraction.x),
                lerp(imageTap(n,corner+int2(0,1)),imageTap(n,corner+int2(1,1)),fraction.x),fraction.y);
}
float pathCoverage(DrawingInstruction n,float2 p)
{
    int winding=0;
    for(uint i=0;i<n.Count;i++)
    {
        float4 edge=arguments.Data.Load(n.First+i);
        float side=cross2(edge.zw-edge.xy,p-edge.xy);
        if(edge.y<=p.y && edge.w>p.y && side>0) winding++;
        if(edge.w<=p.y && edge.y>p.y && side<0) winding--;
    }
    return n.Mode==0 ? (winding!=0 ? 1 : 0) : ((abs(winding)%2)!=0 ? 1 : 0);
}
float meshCoverage(DrawingInstruction n,float2 p)
{
    float result=0;
    for(uint i=0;i<n.Count;i++)
    {
        float4 a=arguments.Data.Load(n.First+i*3),b=arguments.Data.Load(n.First+i*3+1),c=arguments.Data.Load(n.First+i*3+2);
        float area=cross2(b.xy-a.xy,c.xy-a.xy);
        if(abs(area)<1e-20) continue;
        float u=cross2(b.xy-p,c.xy-p)/area;
        float v=cross2(c.xy-p,a.xy-p)/area;
        float w=1-u-v;
        if(u>=0 && v>=0 && w>=0) result=max(result,saturate(u*a.z+v*b.z+w*c.z));
    }
    return result;
}
float4 evaluate(float2 pixel)
{
    float4 values[256];
    for(uint clearIndex=0;clearIndex<256;clearIndex++) values[clearIndex]=0;
    for(uint i=0;i<arguments.Count;i++)
    {
        DrawingInstruction n=arguments.Instructions.Load(i);
        float2 p=float2(dot(n.T0.xyz,float3(pixel,1)),dot(n.T1.xyz,float3(pixel,1)));
        float4 value=0;
        if(n.Kind==0) value=n.A;
        else if(n.Kind==1) value=all(p>=n.A.xy) && all(p<n.A.xy+n.A.zw) ? 1 : 0;
        else if(n.Kind==2)
        {
            float2 halfSize=n.A.zw/2, q=abs(p-n.A.xy-halfSize)-halfSize+n.B.x;
            value=length(max(q,0))+min(max(q.x,q.y),0)<=n.B.x && all(n.A.zw>0) ? 1 : 0;
        }
        else if(n.Kind==3) value=distance(p,n.A.xy)<n.A.z ? 1 : 0;
        else if(n.Kind==4) value=pathCoverage(n,p);
        else if(n.Kind==5)
        {
            float2 center=floor(pixel)+0.5;
            float2 meshPoint=n.Mode==1 ? float2(dot(n.T0.xyz,float3(center,1)),dot(n.T1.xyz,float3(center,1))) : p;
            value=meshCoverage(n,meshPoint);
        }
        else if(n.Kind==6)
        {
            float2 perpendicular=float2(n.B.y-n.A.y,n.A.x-n.B.x);
            float denominator=dot(n.A.zw-n.A.xy,perpendicular);
            value=abs(denominator)>1e-20 ? colorLine(n,dot(p-n.A.xy,perpendicular)/denominator) : float4(0);
        }
        else if(n.Kind==7)
        {
            float2 delta=n.B.xy-n.A.xy, point=p-n.A.xy;
            float dr=n.B.z-n.A.z;
            float a=dot(delta,delta)-dr*dr,b=-2*(dot(point,delta)+n.A.z*dr),c=dot(point,point)-n.A.z*n.A.z;
            float t=-1e30;
            if(abs(a)<1e-12)
            {
                if(abs(b)>1e-12) { float root=-c/b; if(n.A.z+root*dr>=0) t=root; }
            }
            else
            {
                float discriminant=b*b-4*a*c;
                if(discriminant>=0)
                {
                    float root0=(-b-sqrt(discriminant))/(2*a),root1=(-b+sqrt(discriminant))/(2*a);
                    if(n.A.z+root0*dr>=0) t=root0;
                    if(n.A.z+root1*dr>=0) t=max(t,root1);
                }
            }
            value=t>-1e29 ? colorLine(n,t) : float4(0);
        }
        else if(n.Kind==8)
        {
            float angle=atan2(p.y-n.A.y,p.x-n.A.x);
            if(angle<0) angle+=6.28318530718;
            value=n.A.w!=n.A.z ? colorLine(n,(angle-n.A.z)/(n.A.w-n.A.z)) : float4(0);
        }
        else if(n.Kind==9) value=imagePattern(n,p);
        else if(n.Kind==10) value=values[n.First]*n.A.x;
        else if(n.Kind==11) value=values[n.First]*values[n.Count];
        else if(n.Kind==12) value=composite(values[n.First],values[n.Count],n.Mode);
        else if(n.Kind==13 || n.Kind==14)
        {
            if(all(p>=n.A.xy) && all(p<n.A.xy+n.A.zw))
            {
                float2 texel=n.B.xy+(p-n.A.xy)*n.B.zw/n.A.zw;
                float2 dx=float2(n.T0.x,n.T1.x)*n.B.zw/n.A.zw;
                float2 dy=float2(n.T0.y,n.T1.y)*n.B.zw/n.A.zw;
                float4 sample=LumyteSampleGrad(n.Texture,n.Sampler,texel/n.C.xy,dx/n.C.xy,dy/n.C.xy);
                if(n.Kind==13) value=sample.r;
                else
                {
                    float distance=n.Mode==0 ? sample.r : max(min(sample.r,sample.g),min(max(sample.r,sample.g),sample.b));
                    float rate=max(length(dx),length(dy));
                    value=saturate(0.5+(distance-0.5)*n.C.z/max(rate,1e-8));
                }
            }
        }
        values[i]=value;
    }
    return values[arguments.Root];
}
float4 graphSample(GpuTextureRef textureRef,float4 bounds,float2 pixel)
{
    if(any(pixel<bounds.xy) || any(pixel>=bounds.xy+bounds.zw)) return 0;
    return LumyteSampleGrad(textureRef,arguments.GraphSampler,(pixel-bounds.xy)/bounds.zw,0,0);
}
[shader("fragment")]
float4 main(float4 position : SV_Position) : SV_Target0
{
    float4 result=0;
    float totalCoverage=0;
    float2 origin=floor(position.xy);
    for(uint y=0;y<4;y++)
    {
        for(uint x=0;x<4;x++)
        {
            float2 p=origin+(float2(x,y)+0.5)/4;
            float4 sample=evaluate(p);
            if(arguments.GraphMode!=0)
            {
                float coverage=sample.a;
                totalCoverage+=coverage;
                float2 global=p+arguments.PixelOrigin.xy;
                float4 source=graphSample(arguments.SourceTexture,arguments.SourceBounds,global)*arguments.Opacity;
                sample=source*coverage;
                if(arguments.GraphMode==2) sample+=graphSample(arguments.OriginalTexture,arguments.OriginalBounds,global)*(1-coverage);
            }
            result+=sample;
        }
    }
    if(arguments.GraphMode==2 && totalCoverage==0) discard;
    return result/16;
}
