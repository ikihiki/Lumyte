struct EnvironmentRoot { uint textureIndex; uint mode; uint width; uint height; };
[[vk::push_constant]] ConstantBuffer<EnvironmentRoot> root;
static const float pi=3.141592653589793;
[shader("vertex")]
float4 vertexMain(uint id:SV_VertexID):SV_Position
{ return float4(float2((id<<1)&2,id&2)*2-1,0,1); }
float3 direction(float2 uv)
{
    float theta=uv.y*pi,phi=(uv.x-0.5)*2*pi;
    return float3(sin(theta)*cos(phi),cos(theta),sin(theta)*sin(phi));
}
float4 read(Texture2D<float4> image,int2 p,int2 size)
{ return image.Load(int3(((p.x%size.x)+size.x)%size.x,clamp(p.y,0,size.y-1),0)); }
float3 radiance(float3 dir)
{
    Texture2D<float4> image=ResourceDescriptorHeap[root.textureIndex];uint w,h;image.GetDimensions(w,h);
    float2 uv=float2(atan2(dir.z,dir.x)/(2*pi)+0.5,acos(clamp(dir.y,-1,1))/pi);
    float2 p=uv*float2(w,h)-0.5;int2 q=int2(floor(p)),size=int2(w,h);float2 t=frac(p);
    return lerp(lerp(read(image,q,size),read(image,q+int2(1,0),size),t.x),
        lerp(read(image,q+int2(0,1),size),read(image,q+1,size),t.x),t.y).rgb;
}
float2 sequence(uint index,uint count)
{ return float2(float(index)/count,float(reversebits(index))*2.3283064365386963e-10+0.5/count); }
float3 hemisphere(float3 n,float2 xi,float cosine)
{
    float sine=sqrt(max(0,1-cosine*cosine));float phi=2*pi*xi.x;
    float3 up=abs(n.z)<0.999 ? float3(0,0,1) : float3(1,0,0);
    float3 t=normalize(cross(up,n)),b=cross(n,t);
    return t*(sine*cos(phi))+b*(sine*sin(phi))+n*cosine;
}
[shader("pixel")]
float4 pixelMain(float4 position:SV_Position):SV_Target
{
    if(root.mode==2)
    {
        float nv=position.x/root.width,roughness=position.y/root.height,a2=pow(roughness,4);
        float3 v=float3(sqrt(max(0,1-nv*nv)),0,nv);float2 sum=0;
        for(uint i=0;i<512;i++)
        {
            float2 xi=sequence(i,512);float nh=sqrt((1-xi.y)/(1+(a2-1)*xi.y));
            float3 h=hemisphere(float3(0,0,1),xi,nh);float vh=max(dot(v,h),0);
            float3 l=2*vh*h-v;float nl=max(l.z,0);
            if(nl>0)
            {
                float visibility=0.5/max(nl*sqrt(nv*nv*(1-a2)+a2)+nv*sqrt(nl*nl*(1-a2)+a2),1e-6);
                float weight=4*visibility*nl*vh/max(nh,1e-6),fc=pow(1-vh,5);
                sum+=float2(1-fc,fc)*weight;
            }
        }
        return float4(sum/512,0,1);
    }
    float2 uv=position.xy/float2(root.width,root.height);
    float roughness=0;
    if(root.mode==1)
    {
        float layerHeight=float(root.height)/8;uint layer=uint(position.y/layerHeight);
        uv.y=frac(position.y/layerHeight);roughness=float(layer)/7;
    }
    float3 n=direction(uv);
    if(root.mode==1 && roughness==0) return float4(radiance(n),1);
    float3 sum=0;float weight=0;float a2=pow(roughness,4);
    for(uint i=0;i<128;i++)
    {
        float2 xi=sequence(i,128);
        if(root.mode==0)
        { sum+=radiance(hemisphere(n,xi,sqrt(1-xi.y)));weight+=1; }
        else
        {
            float nh=sqrt((1-xi.y)/(1+(a2-1)*xi.y));float3 h=hemisphere(n,xi,nh);
            float3 l=2*dot(n,h)*h-n;float nl=max(dot(n,l),0);
            if(nl>0) { sum+=radiance(l)*nl;weight+=nl; }
        }
    }
    return float4(sum/max(weight,1e-6),1);
}
