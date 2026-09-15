struct ModelRoot { uint geometry; uint parameters; };
[[vk::push_constant]] ConstantBuffer<ModelRoot> root;
float4 vertexData(uint i) { ByteAddressBuffer b = ResourceDescriptorHeap[root.geometry]; return asfloat(b.Load4(i*16)); }
float4 parameter(uint i) { ByteAddressBuffer b = ResourceDescriptorHeap[root.parameters]; return asfloat(b.Load4(i*16)); }
float4 transform(float4 v, uint offset) { return v.x*parameter(offset)+v.y*parameter(offset+1)+v.z*parameter(offset+2)+v.w*parameter(offset+3); }
struct VertexOutput { float4 position : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float4 color : COLOR0; };
[shader("vertex")]
VertexOutput vertexMain(uint id : SV_VertexID)
{
    VertexOutput o;
    float4 world = transform(vertexData(id*3),0);
    o.position = transform(world,4); o.world=world.xyz;
    o.normal = transform(vertexData(id*3+1),8).xyz;
    o.color=vertexData(id*3+2); return o;
}
float3 safeNormal(float3 v) { return v * rsqrt(max(dot(v,v),1e-20)); }
[shader("pixel")]
float4 pixelMain(VertexOutput i, bool front : SV_IsFrontFace) : SV_Target
{
    float4 base = parameter(12)*i.color;
    float4 emission = parameter(13), material = parameter(14), camera = parameter(15);
    if (material.z == 1 && base.a < material.w) discard;
    float alpha = material.z == 2 ? base.a : 1;
    float3 radiance = emission.xyz;
    if (emission.w > 0) radiance = base.rgb;
    else
    {
        float3 n = safeNormal(i.normal) * (front ? 1 : -1), v = parameter(16).w>0 ? parameter(16).xyz : safeNormal(camera.xyz-i.world);
        float nv = max(dot(n,v),0), metallic = material.x;
        float roughness = max(material.y,0.045), a2 = pow(roughness,4);
        float3 f0 = lerp(float3(0.04),base.rgb,metallic);
        for(uint index=0;index<uint(camera.w);index++)
        {
            float4 position=parameter(17+index*4), direction=parameter(18+index*4), color=parameter(19+index*4);
            float3 delta=position.xyz-i.world;
            float d2=max(dot(delta,delta),1e-6);
            float3 l=position.w==0 ? -direction.xyz : delta*rsqrt(d2);
            float attenuation=position.w==0 ? 1 : 1/d2;
            if(position.w!=0 && direction.w>0) attenuation*=saturate(1-pow(sqrt(d2)/direction.w,4));
            if(position.w==2)
            {
                float outer=parameter(20+index*4).x;
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
