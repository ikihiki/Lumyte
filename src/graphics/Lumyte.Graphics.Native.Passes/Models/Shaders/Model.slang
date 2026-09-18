struct ModelRoot { uint geometry; uint parameters; };
[[vk::push_constant]] ConstantBuffer<ModelRoot> root;
float4 vertexData(uint i) { ByteAddressBuffer b = ResourceDescriptorHeap[root.geometry]; return asfloat(b.Load4(i*16)); }
float4 parameter(uint i) { ByteAddressBuffer b = ResourceDescriptorHeap[root.parameters]; return asfloat(b.Load4(i*16)); }
float4 transform(float4 v, uint offset) { return v.x*parameter(offset)+v.y*parameter(offset+1)+v.z*parameter(offset+2)+v.w*parameter(offset+3); }
struct VertexOutput { float4 position : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float4 color : COLOR0; float4 uv01 : TEXCOORD2; float4 uv23 : TEXCOORD3; float2 uv4 : TEXCOORD4; float4 tangent : TEXCOORD5; };
[shader("vertex")]
VertexOutput vertexMain(uint id : SV_VertexID)
{
    VertexOutput o;
    float4 world = transform(vertexData(id*7),0);
    o.position = transform(world,4); o.world=world.xyz;
    o.normal = transform(vertexData(id*7+1),8).xyz;
    o.color=vertexData(id*7+2); o.uv01=vertexData(id*7+3); o.uv23=vertexData(id*7+4); o.uv4=vertexData(id*7+5).xy;
    float4 tangent=vertexData(id*7+6); o.tangent=float4(transform(float4(tangent.xyz,0),0).xyz,tangent.w*parameter(28).w); return o;
}
int wrapTexel(int x, int size, int mode)
{
    if(mode==0) return clamp(x,0,size-1);
    int period=mode==1 ? size*2 : size; int p=((x%period)+period)%period;
    return mode==1 && p>=size ? period-1-p : p;
}
float4 texel(Texture2D<float4> image, int2 p, uint mip, int2 wrap)
{
    uint w,h,levels; image.GetDimensions(mip,w,h,levels);
    return image.Load(int3(wrapTexel(p.x,int(w),wrap.x),wrapTexel(p.y,int(h),wrap.y),mip));
}
float4 sampleLevel(Texture2D<float4> image,float2 uv,uint mip,bool linear,int2 wrap)
{
    uint w,h,levels; image.GetDimensions(mip,w,h,levels); float2 p=uv*float2(w,h)-0.5;
    if(!linear) return texel(image,int2(floor(p+0.5)),mip,wrap);
    int2 q=int2(floor(p)); float2 f=frac(p);
    return lerp(lerp(texel(image,q,mip,wrap),texel(image,q+int2(1,0),mip,wrap),f.x),
        lerp(texel(image,q+int2(0,1),mip,wrap),texel(image,q+1,mip,wrap),f.x),f.y);
}
float4 sampleMaterial(uint slot,float2 inputUv)
{
    uint offset=17+slot*4; float4 info=parameter(offset), sampler=parameter(offset+1), m=parameter(offset+2);
    float2 uv=inputUv.x*m.xy+inputUv.y*m.zw+parameter(offset+3).xy;
    Texture2D<float4> image=ResourceDescriptorHeap[asuint(info.x)];
    uint w,h,levels; image.GetDimensions(0,w,h,levels);
    float2 dx=ddx(uv)*float2(w,h),dy=ddy(uv)*float2(w,h);
    float lod=log2(max(max(length(dx),length(dy)),1e-8)); bool linear=(lod<=0 ? info.w : info.z)>0;
    lod=sampler.x==0 ? 0 : clamp(lod,0,float(levels-1)); int2 wrap=int2(sampler.yz);
    if(sampler.x!=2) return sampleLevel(image,uv,uint(floor(lod+0.5)),linear,wrap);
    uint low=uint(floor(lod)); return lerp(sampleLevel(image,uv,low,linear,wrap),sampleLevel(image,uv,min(low+1,levels-1),linear,wrap),frac(lod));
}
float3 safeNormal(float3 v) { return v * rsqrt(max(dot(v,v),1e-20)); }
[shader("pixel")]
float4 pixelMain(VertexOutput i, bool front : SV_IsFrontFace) : SV_Target
{
    float4 base = parameter(12)*i.color*sampleMaterial(0,i.uv01.xy);
    float4 mr=sampleMaterial(1,i.uv01.zw), emissive=sampleMaterial(4,i.uv4), normalMap=sampleMaterial(2,i.uv23.xy);
    float4 emission = parameter(13), material = parameter(14), camera = parameter(15);
    if (material.z == 1 && base.a < material.w) discard;
    float alpha = material.z == 2 ? base.a : 1;
    float3 radiance = emission.xyz*emissive.rgb;
    if (emission.w > 0) radiance = base.rgb;
    else
    {
        float3 n=safeNormal(i.normal);
        if(parameter(25).y>0)
        {
            float3 t=safeNormal(i.tangent.xyz-n*dot(n,i.tangent.xyz)), b=cross(n,t)*i.tangent.w;
            float3 local=normalMap.xyz*2-1; local.xy*=parameter(28).z;
            n=safeNormal(t*local.x+b*local.y+n*local.z);
        }
        n*=front ? 1 : -1;
        float3 v = parameter(16).w>0 ? parameter(16).xyz : safeNormal(camera.xyz-i.world);
        float nv = max(dot(n,v),0), metallic = material.x*mr.b;
        float roughness = max(material.y*mr.g,0.045), a2 = pow(roughness,4);
        float3 f0 = lerp(float3(0.04),base.rgb,metallic);
        for(uint index=0;index<uint(camera.w);index++)
        {
            float4 position=parameter(37+index*4), direction=parameter(38+index*4), color=parameter(39+index*4);
            float3 delta=position.xyz-i.world;
            float d2=max(dot(delta,delta),1e-6);
            float3 l=position.w==0 ? -direction.xyz : delta*rsqrt(d2);
            float attenuation=position.w==0 ? 1 : 1/d2;
            if(position.w!=0 && direction.w>0) attenuation*=saturate(1-pow(sqrt(d2)/direction.w,4));
            if(position.w==2)
            {
                float outer=parameter(40+index*4).x;
                float spot=saturate((dot(-l,direction.xyz)-outer)/max(color.w-outer,1e-6)); attenuation*=spot*spot;
            }
            float nl=max(dot(n,l),0); float3 h=safeNormal(v+l);
            float nh=max(dot(n,h),0), vh=max(dot(v,h),0);
            float den=nh*nh*(a2-1)+1;
            float distribution=a2/(3.14159265359*den*den);
            float visibility=0.5/max(nl*sqrt(nv*nv*(1-a2)+a2)+nv*sqrt(nl*nl*(1-a2)+a2),1e-6);
            float3 fresnel=f0+(1-f0)*pow(1-vh,5);
            float3 diffuse=(1-fresnel)*(1-metallic)*base.rgb/3.14159265359;
            radiance+=(diffuse+fresnel*distribution*visibility)*color.rgb*(nl*attenuation);
        }
    }
    return float4(radiance*alpha,alpha);
}
