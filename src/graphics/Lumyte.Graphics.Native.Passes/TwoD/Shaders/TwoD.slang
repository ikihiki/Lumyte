// Native renderer data is ordinary scene data; root arguments are always direct.
struct DrawRoot
{
    uint data;
    uint backdrop;
    uint image;
    uint mask;
    uint operation;
    float sigma;
    float axis;
};
[[vk::push_constant]] ConstantBuffer<DrawRoot> root;

float4 scene(uint index) { ByteAddressBuffer values = ResourceDescriptorHeap[root.data]; return asfloat(values.Load4(index * 16)); }
[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * 2 - 1, 0, 1);
}

float2 transformPoint(float2 p, float4 m, float2 t)
{ return float2(p.x*m.x+p.y*m.z, p.x*m.y+p.y*m.w)+t; }

bool pathContains(float2 p, uint start, uint count, bool evenOdd)
{

    int winding = 0;
    for (uint i=0; i<count; ++i)
    {
        float4 edge = scene(start+i);
        float side = (edge.z-edge.x)*(p.y-edge.y)-(p.x-edge.x)*(edge.w-edge.y);
        if (edge.y <= p.y && edge.w > p.y && side > 0) winding++;
        if (edge.w <= p.y && edge.y > p.y && side < 0) winding--;
    }
    return evenOdd ? ((abs(winding)&1)!=0) : winding!=0;
}

bool shapeContains(float2 p)
{

    int kind = int(scene(1).w);
    float4 r = scene(2);
    if (kind == 3) return pathContains(p,uint(scene(8).x),uint(scene(8).y),scene(8).z>0.5);
    float2 q = p-r.xy;
    if (any(q<0) || any(q>=r.zw)) return false;
    if (kind == 0) return true;
    if (kind == 2) { float2 e = (q-r.zw*0.5)/(r.zw*0.5); return dot(e,e)<=1; }
    bool right = q.x>r.z*0.5, bottom=q.y>r.w*0.5;
    float4 radii=scene(3);
    float radius = bottom ? (right?radii.z:radii.w) : (right?radii.y:radii.x);
    float2 center=float2(right?r.z-radius:radius,bottom?r.w-radius:radius);
    float2 delta=abs(q-r.zw*0.5)-r.zw*0.5+radius;
    return dot(max(delta,0),max(delta,0))<=radius*radius;
}

bool clipsContain(float2 p)
{

    uint begin=uint(scene(9).x), count=uint(scene(9).y);
    for(uint i=0;i<count;++i)
    {
        float4 clip=scene(begin+i);
        if (!pathContains(p,uint(clip.x),uint(clip.y),clip.z>0.5)) return false;
    }
    return true;
}

float extendGradient(float t,int mode)
{
    if(mode==1) return t-floor(t);
    if(mode==2) { float x=t-floor(t*0.5)*2; return x<=1?x:2-x; }
    return saturate(t);
}

float4 gradient(float2 p)
{

    int kind=int(scene(7).x);
    if(kind==0) return scene(4);
    float4 a=scene(5), b=scene(6);
    float t=0;
    if(kind==1) { t=dot(p-a.xy,a.zw); }
    else if(kind==2)
    {
        float2 q=p-a.xy, d=a.zw-a.xy;
        float dr=b.y-b.x;
        float A=dot(d,d)-dr*dr, B=-2*(dot(q,d)+b.x*dr), C=dot(q,q)-b.x*b.x;
        if(abs(A)<1e-8) t=abs(B)>1e-8?-C/B:0;
        else { float discriminant=B*B-4*A*C; if(discriminant<0)return float4(0); float z=sqrt(discriminant); float t0=(-B-z)/(2*A),t1=(-B+z)/(2*A); t=max(t0,t1); if(b.x+t*dr<0)t=min(t0,t1); }
    }
    else
    {
        float angle=atan2(p.y-a.y,p.x-a.x);
        if(angle<0) angle+=6.28318530718;
        t=(angle-b.x)/(b.y-b.x);
    }
    t=extendGradient(t,int(scene(7).y));
    uint count=uint(scene(7).z), start=uint(scene(7).w);
    float4 previous=scene(start+1); float previousOffset=scene(start).x;
    if(t<=previousOffset)return previous;
    for(uint i=1;i<count;i++)
    {
        float offset=scene(start+i*2).x; float4 color=scene(start+i*2+1);
        if(t<offset) return lerp(previous,color,saturate((t-previousOffset)/max(offset-previousOffset,1e-20)));
        previous=color; previousOffset=offset;
    }
    return previous;
}

int addressCoordinate(int p, int size, int mode)
{
    if(mode==1) return ((p%size)+size)%size;
    if(mode==2) { int q=((p%(size*2))+size*2)%(size*2);return q<size?q:size*2-1-q; }
    return clamp(p,0,size-1);
}
float4 texel(uint index, int2 p, int2 address, bool transparentOutside)
{
    Texture2D<float4> image = ResourceDescriptorHeap[index];
    uint width,height;image.GetDimensions(width,height);
    if(transparentOutside && (any(p<0)||p.x>=int(width)||p.y>=int(height)))return float4(0);
    return image.Load(int3(addressCoordinate(p.x,int(width),address.x),addressCoordinate(p.y,int(height),address.y),0));
}
float4 sampleImage(uint index,float2 p,bool linear,int2 address,bool transparentOutside)
{
    if(!linear)return texel(index,int2(floor(p)),address,transparentOutside);
    float2 q=p-0.5;int2 at=int2(floor(q));float2 f=frac(q);
    return lerp(lerp(texel(index,at,address,transparentOutside),texel(index,at+int2(1,0),address,transparentOutside),f.x),
        lerp(texel(index,at+int2(0,1),address,transparentOutside),texel(index,at+int2(1,1),address,transparentOutside),f.x),f.y);
}

float softLight(float b){return b<=0.25?((16*b-12)*b+4)*b:sqrt(b);}
float blendComponent(int m,float b,float s)
{
    if(m==14)return b<=0.5?2*b*s:1-2*(1-b)*(1-s);
    if(m==15)return min(b,s);if(m==16)return max(b,s);
    if(m==17)return b<=0?0:(s>=1?1:min(1,b/(1-s)));
    if(m==18)return b>=1?1:(s<=0?0:1-min(1,(1-b)/s));
    if(m==19)return s<=0.5?2*b*s:1-2*(1-b)*(1-s);
    if(m==20)return s<=0.5?b-(1-2*s)*b*(1-b):b+(2*s-1)*(softLight(b)-b);
    if(m==21)return abs(b-s);if(m==22)return b+s-2*b*s;return b*s;
}
float luminosity(float3 c){return dot(c,float3(0.3,0.59,0.11));}
float saturation(float3 c){return max(c.r,max(c.g,c.b))-min(c.r,min(c.g,c.b));}
float3 clipColor(float3 c)
{
    float l=luminosity(c),n=min(c.r,min(c.g,c.b)),x=max(c.r,max(c.g,c.b));
    if(n<0)c=l+(c-l)*l/(l-n);if(x>1)c=l+(c-l)*(1-l)/(x-l);return c;
}
float3 setLuminosity(float3 c,float l){return clipColor(c+l-luminosity(c));}
float3 setSaturation(float3 c,float s){float n=min(c.r,min(c.g,c.b)),x=max(c.r,max(c.g,c.b));return x>n?(c-n)*s/(x-n):float3(0);}
float3 blendColor(int m,float3 b,float3 s)
{
    if(m<=23)return float3(blendComponent(m,b.r,s.r),blendComponent(m,b.g,s.g),blendComponent(m,b.b,s.b));
    if(m==24)return setLuminosity(setSaturation(s,saturation(b)),luminosity(b));
    if(m==25)return setLuminosity(setSaturation(b,saturation(s)),luminosity(b));
    if(m==26)return setLuminosity(s,luminosity(b));return setLuminosity(b,luminosity(s));
}
float4 composite(int m,float4 s,float4 b)
{
    if(m==0)return float4(0);if(m==1)return s;if(m==2)return b;
    if(m==3)return s+b*(1-s.a);if(m==4)return s*(1-b.a)+b;
    if(m==5)return s*b.a;if(m==6)return b*s.a;if(m==7)return s*(1-b.a);if(m==8)return b*(1-s.a);
    if(m==9)return s*b.a+b*(1-s.a);if(m==10)return s*(1-b.a)+b*s.a;
    if(m==11)return s*(1-b.a)+b*(1-s.a);if(m==12)return min(float4(1),s+b);
    float alpha=s.a+b.a-s.a*b.a;
    if(m==13)return float4(s.rgb+b.rgb-s.rgb*b.rgb,alpha);
    float3 straightS=s.a>0?s.rgb/s.a:float3(0),straightB=b.a>0?b.rgb/b.a:float3(0);
    return float4(s.rgb*(1-b.a)+b.rgb*(1-s.a)+blendColor(m,straightB,straightS)*s.a*b.a,alpha);
}

[shader("fragment")]
float4 pixelMain(float4 position : SV_Position) : SV_Target0
{
    if(root.operation==1)
    {
        int radius=int(ceil(root.sigma*3));float4 result=float4(0);float weights=0;
        for(int i=-radius;i<=radius;i++)
        { float weight=exp(-float(i*i)/(2*root.sigma*root.sigma));float2 offset=root.axis<0.5?float2(i,0):float2(0,i);result+=sampleImage(root.image,position.xy+offset,false,0,true)*weight;weights+=weight; }
        return result/max(weights,1e-20);
    }
    if(root.operation==2)return sampleImage(root.image,position.xy,false,0,false);

    float4 backdrop=sampleImage(root.backdrop,position.xy,false,0,false);
    float2 p=transformPoint(position.xy,scene(0),scene(1).xy);
    float4 source=gradient(p);
    if(scene(9).z>0.5)
    {
        float2 q=scene(16).z>0.5?transformPoint(p,scene(15),scene(16).xy):(p-scene(11).xy)/scene(11).zw*scene(10).zw+scene(10).xy;
        source=sampleImage(root.image,q,scene(13).x>0.5,int2(scene(13).y,scene(17).x),scene(13).w>0.5);
        if(scene(13).z>0.5)
        {
            float coverage=source.r;
            if(scene(13).z>1.5){if(scene(13).z>2.5)coverage=max(min(source.r,source.g),min(max(source.r,source.g),source.b));coverage=saturate((coverage-0.5)*scene(14).x+0.5);}
            source=gradient(p)*coverage;
        }
        else if(scene(14).y>0.5)source=scene(12)*source.a;
        else source*=scene(12);
    }
    if(scene(14).w>0.5)
    { Texture2D<float4> maskImage=ResourceDescriptorHeap[root.mask];uint mw,mh;maskImage.GetDimensions(mw,mh);float2 uv=(transformPoint(p,scene(15),scene(16).xy)-scene(18).xy)/scene(18).zw;source*=any(uv<0)||any(uv>=1)?0:sampleImage(root.mask,uv*float2(mw,mh),true,0,false).a; }
    source*=scene(14).z;
    float coverage=0;
    // Paint is evaluated at the pixel center. Subsamples integrate only geometric coverage,
    // avoiding an unintended second filtering operation for layer images.
    [unroll]for(int y=0;y<8;y++)[unroll]for(int x=0;x<8;x++)
    {
        float2 world=floor(position.xy)+(float2(x,y)+0.5)*0.125;
        float2 local=transformPoint(world,scene(0),scene(1).xy);
        if(clipsContain(world)&&shapeContains(local))coverage+=1.0/64;
    }
    return lerp(backdrop,composite(int(scene(9).w),source,backdrop),coverage);
}
