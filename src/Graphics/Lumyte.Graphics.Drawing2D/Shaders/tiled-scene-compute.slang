// Paint/clip evaluator snapshot of draw-records-fragment.slang. No fragment root or entry point.
#include "lumyte.slang"
#ifndef LUMYTE_SMALL_PAINT_GRAPH
#define LUMYTE_SMALL_PAINT_GRAPH 0
#endif
#ifndef LUMYTE_MEDIUM_PAINT_GRAPH
#define LUMYTE_MEDIUM_PAINT_GRAPH 0
#endif
#ifndef LUMYTE_RECORD_MASKS
#define LUMYTE_RECORD_MASKS 1
#endif
#ifndef LUMYTE_RECORD_IMAGES
#define LUMYTE_RECORD_IMAGES 1
#endif
#ifndef LUMYTE_SIMPLE_RECORDS
#define LUMYTE_SIMPLE_RECORDS 0
#endif
struct DrawRecord2D {
    uint Kind; uint Flags; uint PaintId; uint ClipId;
    float4 DeviceBounds; float4 InverseX; float4 InverseY; float4 Shape0;
    uint PayloadOffset16; uint PayloadCount; uint Mode; uint ExtraBits;
};
struct DrawDirectoryEntry2D { uint Kind; uint PayloadOffset16; uint PayloadLength16; uint Flags; };
struct MaskDescriptor2D {
    float4 Source; float4 TextureSizeAndRange;
    uint Encoding; uint Reserved0; uint Reserved1; uint Reserved2;
    GpuTextureRef Texture; GpuSamplerRef Sampler;
};
struct ImagePaintData2D {
    float4 InverseX; float4 InverseY; float4 Source; float4 TextureSizeAndRange;
    uint ChildId; uint TileX; uint TileY; uint Filter;
    uint Alpha; uint FieldMode; uint Reserved0; uint Reserved1;
    GpuTextureRef Texture; GpuSamplerRef Sampler;
};
#ifndef LUMYTE_SHAPE_MEMBERSHIP
#define LUMYTE_SHAPE_MEMBERSHIP 0
#endif
struct DrawPageMetadata2D {
    GpuBufferRef<DrawDirectoryEntry2D> PaintDirectory;
    GpuBufferRef<DrawDirectoryEntry2D> ClipDirectory;
    GpuBufferRef<float4> NumericPayload;

#if LUMYTE_RECORD_MASKS
    GpuBufferRef<MaskDescriptor2D> MaskDescriptors;
#endif
#if LUMYTE_RECORD_IMAGES
    GpuBufferRef<ImagePaintData2D> ImagePaintData;
#endif
#if LUMYTE_SHAPE_MEMBERSHIP
    GpuBufferRef<uint> ShapeMembership;
#endif
};
ImagePaintData2D loadImage(DrawPageMetadata2D page,uint index) {
#if LUMYTE_RECORD_IMAGES
    return page.ImagePaintData.Load(min(index,page.ImagePaintData.Count-1));
#else
    return (ImagePaintData2D)0;
#endif
}
MaskDescriptor2D loadMask(DrawPageMetadata2D page,uint index) {
#if LUMYTE_RECORD_MASKS
    return page.MaskDescriptors.Load(min(index,page.MaskDescriptors.Count-1));
#else
    return (MaskDescriptor2D)0;
#endif
}
// A register-local evaluator view, never a GPU instruction stream.
struct EvaluationNode { float4 T0; float4 T1; float4 A; float4 B; float4 C; float4 D; uint Kind; uint First; uint Count; uint Mode; GpuTextureRef Texture; GpuSamplerRef Sampler; };
float4 numericLoad(GpuBufferRef<float4> data,uint index) { return data.Load(min(index,data.Count-1)); }
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
float3 setLum(float3 c, float l) { return clipColor(c + (l-lum(c))); }
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
    if(mode == 17) return b <= 0 ? 0 : s >= 1 ? 1 : min(1,b/(1-s));
    if(mode == 18) return b >= 1 ? 1 : s <= 0 ? 0 : 1-min(1,(1-b)/s);
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
    if(mode == 12) return saturate(s+b);
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
float4 colorLine(EvaluationNode n, float t, GpuBufferRef<float4> data)
{
    if(n.Count == 0) return 0;
    if(n.Count == 1) return numericLoad(data,n.First+1);
    float start = numericLoad(data,n.First).x;
    float end = numericLoad(data,n.First+(n.Count-1)*2).x;
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
    if(t < start) return numericLoad(data,n.First+1);
    uint left = 0;
    for(uint i=1; i<n.Count; i++)
    {
        float offset = numericLoad(data,n.First+i*2).x;
        if(t < offset)
        {
            float before = numericLoad(data,n.First+left*2).x;
            return lerp(numericLoad(data,n.First+left*2+1), numericLoad(data,n.First+i*2+1), (t-before)/(offset-before));
        }
        left=i;
    }
    return numericLoad(data,n.First+(n.Count-1)*2+1);
}
int tiled(int index, int extent, uint mode)
{
    if(mode==0) return clamp(index,0,extent-1);
    if(mode==3) return index<0 || index>=extent ? -1 : index;
    int period = mode==1 ? extent : extent*2;
    int i = ((index%period)+period)%period;
    return mode==2 && i>=extent ? period-1-i : i;
}
float4 imageTap(EvaluationNode n, int2 pixel)
{
    int x=tiled(pixel.x,int(n.A.z),uint(n.B.z));
    int y=tiled(pixel.y,int(n.A.w),uint(n.B.w));
    if(x<0 || y<0) return 0;
    float2 uv=(n.A.xy+float2(x+0.5,y+0.5))/n.B.xy;
    float4 color=LumyteSampleGrad(n.Texture,n.Sampler,uv,0,0);
    if(n.C.y==0) color.rgb*=color.a;
    return color;
}
float4 imagePattern(EvaluationNode n,float2 p)
{
    if(n.C.x==0) return imageTap(n,int2(floor(p)));
    float2 point=p-0.5, fraction=frac(point);
    int2 corner=int2(floor(point));
    return lerp(lerp(imageTap(n,corner),imageTap(n,corner+int2(1,0)),fraction.x),
                lerp(imageTap(n,corner+int2(0,1)),imageTap(n,corner+int2(1,1)),fraction.x),fraction.y);
}
float distanceFieldCoverage(EvaluationNode n,float2 p)
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
float pathCoverage(EvaluationNode n,float2 p,GpuBufferRef<float4> data)
{
    int winding=0;
    for(uint i=0;i<n.Count;i++)
    {
        float4 edge=numericLoad(data,n.First+i);
        float side=cross2(edge.zw-edge.xy,p-edge.xy);
        if(edge.y<=p.y && edge.w>p.y && side>0) winding++;
        if(edge.w<=p.y && edge.y>p.y && side<0) winding--;
    }
    return n.Mode==0 ? (winding!=0 ? 1 : 0) : ((abs(winding)%2)!=0 ? 1 : 0);
}
float meshCoverage(EvaluationNode n,float2 p,GpuBufferRef<float4> data)
{
    float result=0;
    for(uint i=0;i<n.Count;i++)
    {
        float4 a=numericLoad(data,n.First+i*3),b=numericLoad(data,n.First+i*3+1),c=numericLoad(data,n.First+i*3+2);
        float area=cross2(b.xy-a.xy,c.xy-a.xy);
        if(abs(area)<1e-20) continue;
        float u=cross2(b.xy-p,c.xy-p)/area;
        float v=cross2(c.xy-p,a.xy-p)/area;
        float w=1-u-v;
        if(u>=0 && v>=0 && w>=0) result=max(result,saturate(u*a.z+v*b.z+w*c.z));
    }
    return result;
}

float4 gradient(EvaluationNode n,float2 p,GpuBufferRef<float4> data)
{
    float4 value=0;
        if(n.Kind==6)
        {
            float2 perpendicular=float2(n.B.y-n.A.y,n.A.x-n.B.x);
            float denominator=dot(n.A.zw-n.A.xy,perpendicular);
            value=denominator!=0 ? colorLine(n,dot(p-n.A.xy,perpendicular)/denominator,data) : float4(0);
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
            value=found ? colorLine(n,t,data) : float4(0);
        }
        else if(n.Kind==8)
        {
            float angle=atan2(p.y-n.A.y,p.x-n.A.x);
            if(angle<0) angle+=6.28318530718;
            if(n.A.w!=n.A.z) value=colorLine(n,(angle-n.A.z)/(n.A.w-n.A.z),data);
            else if(n.Mode==0 && n.Count>0)
            {
                // All stop angles coincide for a pad sweep with zero angular span.
                // Rays below that angle use the first color and the rest the last.
                value=numericLoad(data,n.First+(angle<n.A.z ? 1 : (n.Count-1)*2+1));
            }
        }

    return value;
}
uint clipBits(DrawPageMetadata2D page,uint id,float2 origin)
{
    if(id==0) return 65535;
    DrawDirectoryEntry2D entry=page.ClipDirectory.Load(min(id,page.ClipDirectory.Count-1));
    uint4 bounds=asuint(page.NumericPayload.Load(min(entry.PayloadOffset16,page.NumericPayload.Count-1)));
    if(entry.Kind==2 || entry.Kind==3)
    {
        uint bits=0; uint2 base=uint2(origin*4);
        for(uint y=0;y<4;y++) for(uint x=0;x<4;x++)
        {
            uint2 sample=base+uint2(x,y);
            if(all(sample>=bounds.xy) && all(sample<bounds.zw)) bits|=1u<<(y*4+x);
        }
        return bits;
    }
    if(entry.Kind!=4) return 0;
    int2 point=int2(origin)-int2(bounds.xy);
    if(any(point<0) || any(point>=int2(bounds.zw))) return 0;
    uint4 mask=asuint(page.NumericPayload.Load(min(entry.PayloadOffset16+1,page.NumericPayload.Count-1)));
    uint q=uint(point.y)*bounds.z+uint(point.x);
    float4 packed=page.NumericPayload.Load(min(mask.x+q/4,page.NumericPayload.Count-1));
    return uint(packed[q%4]);
}
EvaluationNode imageNode(ImagePaintData2D image)
{
    EvaluationNode n=(EvaluationNode)0;
    n.T0=image.InverseX; n.T1=image.InverseY; n.A=image.Source;
    n.B=float4(image.TextureSizeAndRange.xy,float(image.TileX),float(image.TileY));
    n.C=float4(float(image.Filter),float(image.Alpha),0,0);
    n.D=float4(image.TextureSizeAndRange.z,float(image.FieldMode),0,0);
    n.Texture=image.Texture; n.Sampler=image.Sampler;
    return n;
}
float shapeCoverage(DrawRecord2D r,DrawPageMetadata2D page,MaskDescriptor2D mask,float2 pixel)
{
    float2 p=float2(dot(r.InverseX.xyz,float3(pixel,1)),dot(r.InverseY.xyz,float3(pixel,1)));
#if LUMYTE_SHAPE_MEMBERSHIP
    if((r.Flags&1u)!=0) return 1;
#endif
    if(r.Kind==9) return 1;
    if(r.Kind==1) return all(p>=r.Shape0.xy) && all(p<r.Shape0.xy+r.Shape0.zw) ? 1 : 0;
    if(r.Kind==2)
    {
        float radius=asfloat(r.ExtraBits);
        float2 halfSize=r.Shape0.zw/2,q=abs(p-r.Shape0.xy-halfSize)-halfSize+radius;
        return length(max(q,0))+min(max(q.x,q.y),0)<=radius && all(r.Shape0.zw>0) ? 1 : 0;
    }
    if(r.Kind==3) return distance(p,r.Shape0.xy)<r.Shape0.z ? 1 : 0;
    EvaluationNode n=(EvaluationNode)0;
    n.First=r.PayloadOffset16; n.Count=r.PayloadCount; n.Mode=r.Mode;
    if(r.Kind==4) return pathCoverage(n,p,page.NumericPayload);
    if(r.Kind==5 || r.Kind==6)
    {
        if(r.Kind==6)
        {
            float2 center=floor(pixel)+0.5;
            p=float2(dot(r.InverseX.xyz,float3(center,1)),dot(r.InverseY.xyz,float3(center,1)));
        }
        return meshCoverage(n,p,page.NumericPayload);
    }
    if(any(p<r.Shape0.xy) || any(p>=r.Shape0.xy+r.Shape0.zw)) return 0;
    float2 texel=mask.Source.xy+(p-r.Shape0.xy)*mask.Source.zw/r.Shape0.zw;
    float2 dx=float2(r.InverseX.x,r.InverseY.x)*mask.Source.zw/r.Shape0.zw;
    float2 dy=float2(r.InverseX.y,r.InverseY.y)*mask.Source.zw/r.Shape0.zw;
    float4 sample=LumyteSampleGrad(mask.Texture,mask.Sampler,texel/mask.TextureSizeAndRange.xy,dx/mask.TextureSizeAndRange.xy,dy/mask.TextureSizeAndRange.xy);
    if(r.Kind==7) return sample.r;
    float distance=mask.Encoding==0 ? sample.r : max(min(sample.r,sample.g),min(max(sample.r,sample.g),sample.b));
    return saturate(0.5+(distance-0.5)*mask.TextureSizeAndRange.z/max(max(length(dx),length(dy)),1e-8));
}
uint findId(uint ids[256],uint count,uint id)
{
    for(uint i=0;i<count;i++) if(ids[i]==id) return i;
    return 0;
}
// Discover only the root's closure, once per fragment, not every page node.
uint reachable(DrawPageMetadata2D page,uint root,out uint ids[256])
{
    uint pending[256]; uint pendingCount=1,count=1; pending[0]=root; ids[0]=0;
    while(pendingCount>0)
    {
        uint id=pending[min(--pendingCount,255)];
        if(id==0 || findId(ids,count,id)!=0) continue;
        ids[count++]=id;
        DrawDirectoryEntry2D entry=page.PaintDirectory.Load(min(id,page.PaintDirectory.Count-1));
        uint2 children=0;
        if(entry.Kind==6 || entry.Kind==7 || entry.Kind==8)
        {
            uint4 data=asuint(page.NumericPayload.Load(min(entry.PayloadOffset16,page.NumericPayload.Count-1)));
            children.x=data.x; if(entry.Kind==7) children.y=data.y;
        }
        else if(entry.Kind==9) children.x=loadImage(page,entry.PayloadOffset16/8).ChildId;
        if(children.x!=0) pending[pendingCount++]=children.x;
        if(children.y!=0) pending[pendingCount++]=children.y;
    }
    // Child IDs are smaller. The bounded insertion sort only touches reachable IDs.
    for(uint i=1;i<count;i++) { uint id=ids[i],j=i; while(j>0 && ids[j>0 ? j-1 : 0]>id) { ids[j]=ids[j-1]; j--; } ids[j]=id; }
    return count;
}
EvaluationNode paintLeaf(DrawPageMetadata2D page,DrawDirectoryEntry2D entry)
{
    EvaluationNode n=(EvaluationNode)0;
    uint first=entry.PayloadOffset16;
    if(entry.Kind==1) n.A=page.NumericPayload.Load(min(first,page.NumericPayload.Count-1));
    else if(entry.Kind>=2 && entry.Kind<=4)
    {
        n.T0=page.NumericPayload.Load(min(first,page.NumericPayload.Count-1));
        n.T1=page.NumericPayload.Load(min(first+1,page.NumericPayload.Count-1));
        n.A=page.NumericPayload.Load(min(first+2,page.NumericPayload.Count-1));
        if(entry.Kind!=4) n.B=page.NumericPayload.Load(min(first+3,page.NumericPayload.Count-1));
        uint4 stops=asuint(page.NumericPayload.Load(min(first+(entry.Kind==4 ? 3 : 4),page.NumericPayload.Count-1)));
        n.Kind=entry.Kind+4; n.First=stops.x; n.Count=stops.y; n.Mode=stops.z;
    }
    else if(entry.Kind==5 || entry.Kind==9)
    {
        ImagePaintData2D image=loadImage(page,first/8);
        n=imageNode(image); n.Kind=entry.Kind==5 ? 9 : 15; n.First=image.ChildId;
    }
    return n;
}
float4 leafValue(EvaluationNode n,float2 pixel,GpuBufferRef<float4> numeric)
{
    if(n.Kind==0) return n.A;
    float2 p=float2(dot(n.T0.xyz,float3(pixel,1)),dot(n.T1.xyz,float3(pixel,1)));
    return n.Kind==9 ? imagePattern(n,p) : gradient(n,p,numeric);
}
float4 shadeGraph<let capacity:int>(DrawRecord2D record,DrawPageMetadata2D page,MaskDescriptor2D mask,uint outer,float2 origin,uint ids[256],uint count)
{
    // These are register/private-memory evaluator views, not a GPU stream.
    // Decode each reachable immutable directory/header/typed descriptor once.
    EvaluationNode nodes[capacity];
    for(uint i=1;i<count;i++)
    {
        uint at=min(i,uint(capacity-1));
        DrawDirectoryEntry2D entry=page.PaintDirectory.Load(min(ids[min(i,255)],page.PaintDirectory.Count-1));
        EvaluationNode n=(EvaluationNode)0;
        if(entry.Kind<=5 || entry.Kind==9)
        {
            n=paintLeaf(page,entry);
            if(entry.Kind==9) n.First=findId(ids,count,n.First);
        }
        else
        {
            float4 cell=page.NumericPayload.Load(min(entry.PayloadOffset16,page.NumericPayload.Count-1)); uint4 data=asuint(cell);
            n.First=findId(ids,count,data.x);
            if(entry.Kind==6) { n.Kind=10; n.A=float4(cell.y,0,0,0); }
            else if(entry.Kind==7) { n.Kind=12; n.Count=findId(ids,count,data.y); n.Mode=data.z; }
            else if(entry.Kind==8) { n.Kind=18; n.Mode=clipBits(page,data.y,origin); }
        }
        nodes[at]=n;
    }
    float4 result=0;
    for(uint sample=0;sample<16;sample++)
    {
        if(((outer>>sample)&1)==0) continue;
        float2 pixel=origin+(float2(sample%4,sample/4)+0.5)/4;
        float4 values[capacity]; values[0]=0;
        for(uint i=1;i<count;i++)
        {
            uint at=min(i,uint(capacity-1)); EvaluationNode n=nodes[at];
            float4 value=0;
            if(n.Kind==10) value=values[min(n.First,uint(capacity-1))]*n.A.x;
            else if(n.Kind==12) value=composite(values[min(n.First,uint(capacity-1))],values[min(n.Count,uint(capacity-1))],n.Mode);
            else if(n.Kind==18) value=((n.Mode>>sample)&1)!=0 ? values[min(n.First,uint(capacity-1))] : float4(0);
            else if(n.Kind==15)
            {
                float2 p=float2(dot(n.T0.xyz,float3(pixel,1)),dot(n.T1.xyz,float3(pixel,1)));
                value=values[min(n.First,uint(capacity-1))]*distanceFieldCoverage(n,p);
            }
            else value=leafValue(n,pixel,page.NumericPayload);
            values[at]=value;
        }
        result+=shapeCoverage(record,page,mask,pixel)*values[min(count-1,uint(capacity-1))];
    }
    return result/16;
}

struct ComputeExecutionArguments2D {
    GpuBufferRef<DrawRecord2D> Records;
    GpuBufferRef<DrawPageMetadata2D> PageMetadata;
    GpuBufferRef<float4> Input;
    GpuRWBufferRef<float4> Storage;
    GpuRWBufferRef<uint> Work;
    uint Stage; uint Reserved0; uint Reserved1; uint Reserved2;
};
ConstantBuffer<ComputeExecutionArguments2D> arguments;
uint configuration(uint index) { return asuint(arguments.Input.Load(index/4))[index%4]; }
float4 descriptor(uint draw,uint cell) { return arguments.Input.Load(4+draw*8+cell); }
uint4 words(uint draw,uint cell) { return asuint(descriptor(draw,cell)); }
float2 transformed(uint draw,float2 p) {
    return float2(dot(descriptor(draw,3).xyz,float3(p,1)),dot(descriptor(draw,4).xyz,float3(p,1)));
}
void failure() { arguments.Work.Store(0,1); }
void storeGeometry(uint at,float4 value) {
    if(any(!isfinite(value)) || at>=configuration(8) || at>=arguments.Storage.Count) { failure(); return; }
    arguments.Storage.Store(at,value);
}
float2 bezier(float2 a,float2 b,float2 c,float2 d,uint verb,float t) {
    if(verb==2) return lerp(lerp(a,b,t),lerp(b,c,t),t);
    if(verb==3) return lerp(lerp(lerp(a,b,t),lerp(b,c,t),t),lerp(lerp(b,c,t),lerp(c,d,t),t),t);
    return lerp(a,b,t);
}
void generate(uint index) {
    if(index>=configuration(1)) return;
    uint base=4+configuration(0)*8+index*3;
    float4 ab=arguments.Input.Load(base),cd=arguments.Input.Load(base+1);
    uint4 info=asuint(arguments.Input.Load(base+2));
    uint draw=info.w; uint4 raw=words(draw,0); uint4 geo=words(draw,1);
    bool stroke=words(draw,6).x!=0;
    float2 a=ab.xy,b=ab.zw,c=cd.xy,d=cd.zw;
    // Transform controls before de Casteljau for fills; strokes retain logical
    // edges because width, caps and joins precede an arbitrary affine transform.
    if(!stroke) { a=transformed(draw,a); b=transformed(draw,b); c=transformed(draw,c); d=transformed(draw,d); }
    for(uint segment=0;segment<info.y;segment++) {
        float2 p=bezier(a,b,c,d,info.x,float(segment)/info.y);
        float2 q=bezier(a,b,c,d,info.x,float(segment+1)/info.y);
        uint at=(stroke?raw.z:geo.x)+info.z+segment;
        storeGeometry(at,float4(p,q));
    }
}
void triangle(uint draw,inout uint count,float2 a,float2 b,float2 c) {
    uint4 geo=words(draw,1);
    if(count>=geo.y) { failure(); return; }
    // Preserve the affine transform; membership is a union, not a winding sum.
    float2 p=transformed(draw,a),q=transformed(draw,b),r=transformed(draw,c);
    uint at=geo.x+count*3;
    storeGeometry(at,float4(p,0,0)); storeGeometry(at+1,float4(q,0,0)); storeGeometry(at+2,float4(r,0,0)); count++;
}
void disc(uint draw,inout uint count,float2 center) {
    uint divisions=words(draw,6).y; float half=descriptor(draw,5).x*0.5;
    for(uint i=0;i<divisions;i++) {
        float a=6.283185307179586*float(i)/divisions,b=6.283185307179586*float(i+1)/divisions;
        triangle(draw,count,center,center+float2(cos(a),sin(a))*half,center+float2(cos(b),sin(b))*half);
    }
}
float2 stableDirection(float2 v) { float scale=max(abs(v.x),abs(v.y)); return normalize(v/scale); }
void joinStroke(uint draw,inout uint count,float2 center,float2 before,float2 after) {
    float4 style=descriptor(draw,5); float half=style.x*0.5;
    if(uint(style.w)==1) { disc(draw,count,center); return; }
    float turn=cross2(before,after); if(abs(turn)<0.000001) return;
    float sign=turn>0?-1:1;
    float2 a=center+float2(-before.y,before.x)*(half*sign),b=center+float2(-after.y,after.x)*(half*sign);
    if(uint(style.w)==0) {
        float2 m=a+before*(cross2(b-a,after)/turn);
        if(distance(m,center)<=style.y*half) { triangle(draw,count,a,m,center); triangle(draw,count,m,b,center); return; }
    }
    triangle(draw,count,a,b,center);
}
void expand(uint draw) {
    if(draw>=configuration(0)) return;
    uint4 raw=words(draw,0),geo=words(draw,1);
    if(words(draw,6).x==0) { arguments.Work.Store(1+draw,geo.y); return; }
    uint count=0; float4 style=descriptor(draw,5); float half=style.x*0.5;
    uint curveBase=4+configuration(0)*8;
    for(uint k=0;k<raw.y;k++) {
        uint at=curveBase+(raw.x+k)*3;
        uint4 info=asuint(arguments.Input.Load(at+2)); if(info.x!=0) continue;
        float2 point=arguments.Input.Load(at).xy;
        uint begin=info.z,end=raw.w; bool closed=false;
        for(uint j=k+1;j<raw.y;j++) {
            uint4 next=asuint(arguments.Input.Load(curveBase+(raw.x+j)*3+2));
            if(next.x==0) { end=next.z; break; }
            if(next.x==4) closed=true;
        }
        uint first=end,last=end;
        for(uint e=begin;e<end;e++) {
            float4 line=arguments.Storage.Load(raw.z+e);
            if(any(line.xy!=line.zw)) { if(first==end) first=e; last=e; }
        }
        if(first==end) {
            if(uint(style.z)==1) disc(draw,count,point);
            if(uint(style.z)==2) {
                triangle(draw,count,point+float2(-half,-half),point+float2(half,-half),point+float2(half,half));
                triangle(draw,count,point+float2(-half,-half),point+float2(half,half),point+float2(-half,half));
            }
            continue;
        }
        uint previous=closed?last:end;
        for(uint e=first;e<=last;e++) {
            float4 line=arguments.Storage.Load(raw.z+e); if(all(line.xy==line.zw)) continue;
            float2 direction=stableDirection(line.zw-line.xy),normal=float2(-direction.y,direction.x)*half;
            float2 a=line.xy,b=line.zw;
            if(!closed && uint(style.z)==2) { if(e==first) a-=direction*half; if(e==last) b+=direction*half; }
            triangle(draw,count,a+normal,a-normal,b+normal); triangle(draw,count,b+normal,a-normal,b-normal);
            if(previous!=end) {
                float4 prev=arguments.Storage.Load(raw.z+previous);
                joinStroke(draw,count,line.xy,stableDirection(prev.zw-prev.xy),direction);
            }
            previous=e;
        }
        if(!closed && uint(style.z)==1) {
            disc(draw,count,arguments.Storage.Load(raw.z+first).xy);
            disc(draw,count,arguments.Storage.Load(raw.z+last).zw);
        }
    }
    arguments.Work.Store(1+draw,count);
}
uint tileOwner(uint tile) {
    for(uint draw=0;draw<configuration(0);draw++) { uint4 g=words(draw,1); if(tile>=g.z && tile-g.z<g.w) return draw; }
    return configuration(0);
}
bool touches(uint draw,uint primitive,float2 lo,float2 hi) {
    uint at=words(draw,1).x+primitive*(words(draw,6).x!=0?3:1);
    float4 e=arguments.Storage.Load(at); float2 a=e.xy,b=e.zw;
    float2 low=min(a,b),high=max(a,b);
    if(words(draw,6).x!=0) {
        b=arguments.Storage.Load(at+1).xy; float2 c=arguments.Storage.Load(at+2).xy;
        low=min(a,min(b,c)); high=max(a,max(b,c));
    }
    return all(high>=lo) && all(low<=hi);
}
void binTile(uint tile,bool fill) {
    if(tile>=configuration(4)) return;
    uint draw=tileOwner(tile); if(draw==configuration(0)) { failure(); return; }
    uint4 g=words(draw,1); int4 bounds=asint(descriptor(draw,2)); uint local=tile-g.z;
    float2 lo=float2(bounds.xy+int2(local%uint(bounds.z),local/uint(bounds.z)))*16;
    uint count=0,start=fill?arguments.Work.Load(configuration(10)+tile):0;
    uint primitives=arguments.Work.Load(1+draw);
    for(uint edge=0;edge<primitives;edge++) if(touches(draw,edge,lo,lo+16)) {
        if(fill) {
            if(start+count>=configuration(6)) { failure(); return; }
            arguments.Work.Store(configuration(11)+start+count,edge);
        }
        count++;
    }
    if(!fill) arguments.Work.Store(configuration(9)+tile,count);
}
void scan(bool candidates) {
    uint total=0,number=candidates?configuration(5):configuration(4);
    uint counts=candidates?configuration(13):configuration(9);
    uint prefixes=candidates?configuration(14):configuration(10);
    uint capacity=candidates?configuration(7):configuration(6);
    for(uint i=0;i<number;i++) {
        arguments.Work.Store(prefixes+i,total); uint count=arguments.Work.Load(counts+i);
        if(total>capacity || count>capacity-total) { failure(); return; } total+=count;
    }
}
int crossing(float4 edge,float y,float left,float right) {
    if(!((edge.y<=y && edge.w>y)||(edge.w<=y && edge.y>y))) return 0;
    float x=edge.x+(y-edge.y)*(edge.z-edge.x)/(edge.w-edge.y);
    return x>left && x<=right ? (edge.w>edge.y?1:-1):0;
}
void backdrop(uint index) {
    // One invocation owns one sample row and sequentially propagates its winding
    // across columns. Cross-workgroup ordering is provided by separate dispatches.
    uint row=index/64,sample=index%64;
    uint rows=0;
    for(uint draw=0;draw<configuration(0);draw++) {
        int4 b=asint(descriptor(draw,2)); if(words(draw,6).x!=0) continue;
        if(row>=rows+uint(b.w)) { rows+=uint(b.w); continue; }
        uint y=row-rows; int winding=0; float py=float(b.y*16+int(y)*16)+(float(sample)+0.5)/4;
        uint4 g=words(draw,1);
        for(uint x=0;x<uint(b.z);x++) {
            uint tile=g.z+y*uint(b.z)+x;
            arguments.Work.Store(configuration(12)+tile*64+sample,asuint(winding));
            uint start=arguments.Work.Load(configuration(10)+tile),count=arguments.Work.Load(configuration(9)+tile);
            float left=float((b.x+int(x))*16);
            for(uint i=0;i<count;i++) {
                uint e=arguments.Work.Load(configuration(11)+start+i);
                winding+=crossing(arguments.Storage.Load(g.x+e),py,left,left+16);
            }
        }
        return;
    }
}
bool candidate(uint draw,uint tile) {
    uint columns=(configuration(2)+15)/16; float2 lo=float2(tile%columns,tile/columns)*16;
    float4 b=arguments.Records.Load(draw).DeviceBounds;
    return all(b.zw>lo) && all(b.xy<lo+16);
}
void candidates(uint tile,bool fill) {
    if(tile>=configuration(5)) return;
    uint start=fill?arguments.Work.Load(configuration(14)+tile):0,count=0;
    // Stable ascending ordinal, never sorted by texture/paint or append arrival.
    for(uint draw=0;draw<configuration(0);draw++) if(candidate(draw,tile)) {
        if(fill) {
            if(start+count>=configuration(7)) { failure(); return; }
            arguments.Work.Store(configuration(15)+start+count,draw);
        }
        count++;
    }
    if(!fill) arguments.Work.Store(configuration(13)+tile,count);
}
uint membership(uint draw,float2 origin) {
    uint4 g=words(draw,1); if(g.w==0) return 65535;
    int4 b=asint(descriptor(draw,2)); int2 tile=int2(floor(origin/16))-b.xy;
    if(any(tile<0)||tile.x>=b.z||tile.y>=b.w) return 0;
    uint t=g.z+uint(tile.y*b.z+tile.x),count=arguments.Work.Load(configuration(9)+t),start=arguments.Work.Load(configuration(10)+t);
    uint bits=0;
    for(uint sample=0;sample<16;sample++) {
        float2 p=origin+(float2(sample%4,sample/4)+0.5)/4;
        if(words(draw,6).x==0) {
            uint row=uint(origin.y-float((b.y+tile.y)*16))*4+sample/4;
            int winding=asint(arguments.Work.Load(configuration(12)+t*64+row));
            float left=float((b.x+tile.x)*16);
            for(uint i=0;i<count;i++) {
                uint e=arguments.Work.Load(configuration(11)+start+i);
                winding+=crossing(arguments.Storage.Load(g.x+e),p.y,left,p.x);
            }
            if(words(draw,6).z==0?winding!=0:(abs(winding)&1)!=0) bits|=1u<<sample;
        } else {
            for(uint i=0;i<count;i++) {
                uint e=arguments.Work.Load(configuration(11)+start+i),at=g.x+e*3;
                float2 a=arguments.Storage.Load(at).xy,c=arguments.Storage.Load(at+2).xy,d=arguments.Storage.Load(at+1).xy;
                float area=cross2(d-a,c-a); if(abs(area)<1e-20) continue;
                float u=cross2(d-p,c-p)/area,v=cross2(c-p,a-p)/area,w=1-u-v;
                if(u>=0 && v>=0 && w>=0) { bits|=1u<<sample; break; }
            }
        }
    }
    return bits;
}
float4 shadeDraw(uint draw,DrawPageMetadata2D page,float2 origin) {
    DrawRecord2D record=arguments.Records.Load(draw);
    if(any(origin+1<=record.DeviceBounds.xy)||any(origin>=record.DeviceBounds.zw)) return 0;
    uint outer=clipBits(page,record.ClipId,origin)&membership(draw,origin);
    if(outer==0) return 0;
    DrawDirectoryEntry2D root=page.PaintDirectory.Load(record.PaintId);
    MaskDescriptor2D mask=(MaskDescriptor2D)0;
    if(record.Kind==7||record.Kind==8) mask=loadMask(page,record.PayloadOffset16/5);
    if(root.Kind<=5) {
        EvaluationNode leaf=paintLeaf(page,root); float4 result=0;
        for(uint sample=0;sample<16;sample++) if(((outer>>sample)&1)!=0) {
            float2 pixel=origin+(float2(sample%4,sample/4)+0.5)/4;
            result+=shapeCoverage(record,page,mask,pixel)*leafValue(leaf,pixel,page.NumericPayload);
        }
        return result/16;
    }
    uint ids[256]; uint count=reachable(page,record.PaintId,ids);
    return count<=8?shadeGraph<8>(record,page,mask,outer,origin,ids,count):count<=16?shadeGraph<16>(record,page,mask,outer,origin,ids,count):shadeGraph<256>(record,page,mask,outer,origin,ids,count);
}
void shade(uint index) {
    if(index>=configuration(2)*configuration(3)) return;
    uint2 p=uint2(index%configuration(2),index/configuration(2)),tile=p/16;
    uint t=tile.y*((configuration(2)+15)/16)+tile.x;
    uint start=arguments.Work.Load(configuration(14)+t),count=arguments.Work.Load(configuration(13)+t);
    float4 color=0;
    DrawPageMetadata2D page=arguments.PageMetadata.Load(0);
    for(uint i=0;i<count;i++) {
        uint draw=arguments.Work.Load(configuration(15)+start+i);
        float4 value=shadeDraw(draw,page,float2(p));
        // Average each complete draw BEFORE scene SrcOver (not per-sample scene AA).
        color=value+color*(1-value.a);
    }
    // Capacity violations cannot write outside reservations or masquerade as
    // approximate success. A diagnostic flag and nonfinite output expose failure.
    if(arguments.Work.Load(0)!=0) color=asfloat(uint4(0x7fc00000));
    arguments.Storage.Store(configuration(8)+index,color);
}
[shader("compute")]
[numthreads(16,16,1)]
void main(uint3 group:SV_GroupID,uint lane:SV_GroupIndex) {
    uint index=group.x*256+lane;
    if(arguments.Stage==0) {
        if(index<arguments.Work.Count) arguments.Work.Store(index,0);
        if(index<arguments.Storage.Count) arguments.Storage.Store(index,float4(0));
    }
    else if(arguments.Stage==1) generate(index);
    else if(arguments.Stage==2) expand(index);
    else if(arguments.Stage==3) binTile(index,false);
    else if(arguments.Stage==4) { if(index==0) scan(false); }
    else if(arguments.Stage==5) binTile(index,true);
    else if(arguments.Stage==6) backdrop(index);
    else if(arguments.Stage==7) candidates(index,false);
    else if(arguments.Stage==8) { if(index==0) scan(true); }
    else if(arguments.Stage==9) candidates(index,true);
    else if(arguments.Stage==10) shade(index);
}
