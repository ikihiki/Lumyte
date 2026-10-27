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
float4 colorLine(DrawingInstruction n, float t)
{
    if(n.Count == 0) return 0;
    if(n.Count == 1) return arguments.Data.Load(n.First+1);
    float start = arguments.Data.Load(n.First).x;
    float end = arguments.Data.Load(n.First+(n.Count-1)*2).x;
    float range = end-start;
    // A zero-width repeated color line has no well-defined period. The pad case
    // remains a hard transition from the first stop below to the last at/above it.
    if(range==0 && n.Mode!=0) return 0;
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
float distanceFieldCoverage(DrawingInstruction n,float2 p)
{
    float4 distances=imagePattern(n,p);
    float sd=max(min(distances.r,distances.g),min(max(distances.r,distances.g),distances.b));
    // This is the affine equivalent of fwidth(sourceTexel), independent of divergent
    // paint evaluation. Distances are numerical data, never decoded as sRGB or alpha-premultiplied.
    float2 footprint=max(abs(float2(n.T0.x,n.T1.x))+abs(float2(n.T0.y,n.T1.y)),float2(1e-10));
    if(n.D.y==1)
    {
        // Singular values distinguish real minification from rotation: a 1:1 rotated
        // glyph must retain RGB sharp corners even though its fwidth exceeds one texel.
        float a=dot(n.T0.xy,n.T0.xy),b=dot(n.T0.xy,n.T1.xy),c=dot(n.T1.xy,n.T1.xy);
        float maximumScale=sqrt(max(0,0.5*(a+c+sqrt((a-c)*(a-c)+4*b*b))));
        sd=lerp(sd,distances.a,saturate(maximumScale-1));
    }
    float screenRange=n.D.x*0.5*(1/footprint.x+1/footprint.y);
    // evaluate is integrated over 4x4 correlated shape/clip samples. A quarter-pixel
    // ramp here avoids applying a full-pixel reconstruction filter sixteen times.
    return saturate((sd-0.5)*screenRange*4+0.5);
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
float blendLuminosity(float3 color) { return dot(color,float3(0.3,0.59,0.11)); }
float blendSaturation(float3 color) { return max(color.r,max(color.g,color.b))-min(color.r,min(color.g,color.b)); }
float3 blendClipColor(float3 color)
{
    float luminosity=blendLuminosity(color);
    float minimum=min(color.r,min(color.g,color.b)),maximum=max(color.r,max(color.g,color.b));
    if(minimum<0) color=luminosity+(color-luminosity)*luminosity/max(luminosity-minimum,1e-20);
    if(maximum>1) color=luminosity+(color-luminosity)*(1-luminosity)/max(maximum-luminosity,1e-20);
    return color;
}
float3 blendSetLuminosity(float3 color,float luminosity)
{
    return blendClipColor(color+(luminosity-blendLuminosity(color)));
}
float3 blendSetSaturation(float3 color,float saturation)
{
    float minimum=min(color.r,min(color.g,color.b)),maximum=max(color.r,max(color.g,color.b));
    return maximum>minimum ? (color-minimum)*saturation/(maximum-minimum) : float3(0);
}
float blendComponent(float backdrop,float source,uint mode)
{
    if(mode==13) return backdrop+source-backdrop*source;
    if(mode==14) return backdrop<=0.5 ? 2*backdrop*source : 1-2*(1-backdrop)*(1-source);
    if(mode==15) return min(backdrop,source);
    if(mode==16) return max(backdrop,source);
    if(mode==17)
    {
        if(backdrop<=0) return 0;
        return source>=1 ? 1 : min(1,backdrop/(1-source));
    }
    if(mode==18)
    {
        if(backdrop>=1) return 1;
        return source<=0 ? 0 : 1-min(1,(1-backdrop)/source);
    }
    if(mode==19) return source<=0.5 ? 2*backdrop*source : 1-2*(1-backdrop)*(1-source);
    if(mode==20)
    {
        float d=backdrop<=0.25 ? ((16*backdrop-12)*backdrop+4)*backdrop : sqrt(max(backdrop,0));
        return source<=0.5 ? backdrop-(1-2*source)*backdrop*(1-backdrop) : backdrop+(2*source-1)*(d-backdrop);
    }
    if(mode==21) return abs(backdrop-source);
    if(mode==22) return backdrop+source-2*backdrop*source;
    return backdrop*source;
}
float3 blendColor(float3 backdrop,float3 source,uint mode)
{
    if(mode==24) return blendSetLuminosity(blendSetSaturation(source,blendSaturation(backdrop)),blendLuminosity(backdrop));
    if(mode==25) return blendSetLuminosity(blendSetSaturation(backdrop,blendSaturation(source)),blendLuminosity(backdrop));
    if(mode==26) return blendSetLuminosity(source,blendLuminosity(backdrop));
    if(mode==27) return blendSetLuminosity(backdrop,blendLuminosity(source));
    return float3(blendComponent(backdrop.r,source.r,mode),blendComponent(backdrop.g,source.g,mode),blendComponent(backdrop.b,source.b,mode));
}
float4 compositePaint(float4 source,float4 backdrop,uint mode)
{
    if(mode==0) return 0;
    if(mode==1) return source;
    if(mode==2) return backdrop;
    if(mode==3) return source+backdrop*(1-source.a);
    if(mode==4) return backdrop+source*(1-backdrop.a);
    if(mode==5) return source*backdrop.a;
    if(mode==6) return backdrop*source.a;
    if(mode==7) return source*(1-backdrop.a);
    if(mode==8) return backdrop*(1-source.a);
    if(mode==9) return source*backdrop.a+backdrop*(1-source.a);
    if(mode==10) return backdrop*source.a+source*(1-backdrop.a);
    if(mode==11) return source*(1-backdrop.a)+backdrop*(1-source.a);
    if(mode==12) return saturate(source+backdrop);
    float3 cs=source.a>0 ? source.rgb/source.a : float3(0);
    float3 cb=backdrop.a>0 ? backdrop.rgb/backdrop.a : float3(0);
    float3 rgb=source.rgb*(1-backdrop.a)+backdrop.rgb*(1-source.a)+source.a*backdrop.a*blendColor(cb,cs,mode);
    return float4(rgb,source.a+backdrop.a-source.a*backdrop.a);
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
        else if(n.Kind==6)
        {
            float2 perpendicular=float2(n.B.y-n.A.y,n.A.x-n.B.x);
            float denominator=dot(n.A.zw-n.A.xy,perpendicular);
            value=denominator!=0 ? colorLine(n,dot(p-n.A.xy,perpendicular)/denominator) : float4(0);
        }
        else if(n.Kind==7)
        {
            float2 delta=n.B.xy-n.A.xy, point=p-n.A.xy;
            // Rescale the equation before squaring so tiny gradients retain their
            // geometry and large finite design coordinates do not overflow.
            float scale=max(max(max(abs(delta.x),abs(delta.y)),max(abs(point.x),abs(point.y))),max(abs(n.A.z),abs(n.B.z)));
            scale=max(scale,1e-30);
            delta/=scale; point/=scale;
            float r0=n.A.z/scale, dr=(n.B.z/scale)-r0;
            float a=dot(delta,delta)-dr*dr,b=-2*(dot(point,delta)+r0*dr),c=dot(point,point)-r0*r0;
            float t=0;
            bool found=false;
            if(a==0)
            {
                if(b!=0) { float root=-c/b; if(r0+root*dr>0) { t=root; found=true; } }
            }
            else
            {
                float discriminant=b*b-4*a*c;
                if(discriminant>=0)
                {
                    // Use the stable quadratic form, then keep the largest root
                    // whose interpolated radius is strictly positive (COLR rule).
                    float q=-0.5*(b+(b>=0 ? sqrt(discriminant) : -sqrt(discriminant)));
                    float root0=q/a,root1=q!=0 ? c/q : root0;
                    if(r0+root0*dr>0) { t=root0; found=true; }
                    if(r0+root1*dr>0 && (!found || root1>t)) { t=root1; found=true; }
                }
            }
            value=found ? colorLine(n,t) : float4(0);
        }
        else if(n.Kind==8)
        {
            float angle=atan2(p.y-n.A.y,p.x-n.A.x);
            if(angle<0) angle+=6.28318530718;
            if(n.A.w!=n.A.z) value=colorLine(n,(angle-n.A.z)/(n.A.w-n.A.z));
            else if(n.Mode==0 && n.Count>0)
            {
                // All stop angles coincide for a pad sweep with zero angular span.
                // Rays below that angle use the first color and the rest the last.
                value=arguments.Data.Load(n.First+(angle<n.A.z ? 1 : (n.Count-1)*2+1));
            }
        }
        else if(n.Kind==9) value=imagePattern(n,p);
        else if(n.Kind==10) value=values[n.First]*n.A.x;
        else if(n.Kind==11) value=values[n.First]*values[n.Count];
        else if(n.Kind==12) value=values[n.First]*distanceFieldCoverage(n,p);
        else if(n.Kind==13) value=compositePaint(values[n.First],values[n.Count],n.Mode);
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
