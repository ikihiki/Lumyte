requires immediate_address_space;
struct Root { offset: u32, reserved: u32 }
var<immediate> root: Root;
@group(0) @binding(0) var<storage,read> geometry: array<vec4f>;
@group(0) @binding(1) var<storage,read> parameters: array<vec4f>;
@group(0) @binding(2) var baseImage: texture_2d<f32>;
@group(0) @binding(3) var mrImage: texture_2d<f32>;
@group(0) @binding(4) var normalImage: texture_2d<f32>;
@group(0) @binding(5) var occlusionImage: texture_2d<f32>;
@group(0) @binding(6) var emissiveImage: texture_2d<f32>;
fn parameter(i: u32) -> vec4f { return parameters[root.offset+i]; }
fn transform(v: vec4f, offset: u32) -> vec4f { return v.x*parameter(offset)+v.y*parameter(offset+1)+v.z*parameter(offset+2)+v.w*parameter(offset+3); }
struct VertexOutput { @builtin(position) position: vec4f, @location(0) world: vec3f, @location(1) normal: vec3f, @location(2) color: vec4f,
    @location(3) uv01: vec4f, @location(4) uv23: vec4f, @location(5) uv4: vec2f }
@vertex fn vertex(@builtin(vertex_index) id: u32) -> VertexOutput {
    let world=transform(geometry[id*6],0);
    return VertexOutput(transform(world,4),world.xyz,transform(geometry[id*6+1],8).xyz,geometry[id*6+2],geometry[id*6+3],geometry[id*6+4],geometry[id*6+5].xy);
}
fn wrapTexel(x: i32,size: i32,mode: i32) -> i32 {
    if mode==0 { return clamp(x,0,size-1); }
    let period=select(size,size*2,mode==1); let p=((x%period)+period)%period;
    return select(p,period-1-p,mode==1 && p>=size);
}
fn texel(image: texture_2d<f32>,p: vec2i,mip: u32,wrap: vec2i) -> vec4f {
    let size=vec2i(textureDimensions(image,mip));
    return textureLoad(image,vec2i(wrapTexel(p.x,size.x,wrap.x),wrapTexel(p.y,size.y,wrap.y)),i32(mip));
}
fn sampleLevel(image: texture_2d<f32>,uv: vec2f,mip: u32,linear: bool,wrap: vec2i) -> vec4f {
    let p=uv*vec2f(textureDimensions(image,mip))-0.5;
    if !linear { return texel(image,vec2i(floor(p+0.5)),mip,wrap); }
    let q=vec2i(floor(p)); let f=fract(p);
    return mix(mix(texel(image,q,mip,wrap),texel(image,q+vec2i(1,0),mip,wrap),f.x),
        mix(texel(image,q+vec2i(0,1),mip,wrap),texel(image,q+vec2i(1),mip,wrap),f.x),f.y);
}
fn sampleMaterial(image: texture_2d<f32>,slot: u32,inputUv: vec2f) -> vec4f {
    let offset=17+slot*4; let info=parameter(offset); let sampling=parameter(offset+1); let m=parameter(offset+2);
    let uv=inputUv.x*m.xy+inputUv.y*m.zw+parameter(offset+3).xy;
    let size=vec2f(textureDimensions(image)); let levels=textureNumLevels(image);
    let dx=dpdx(uv)*size; let dy=dpdy(uv)*size;
    var lod=log2(max(max(length(dx),length(dy)),1e-8)); let linear=select(info.z,info.w,lod<=0)>0;
    lod=select(clamp(lod,0.0,f32(levels-1)),0.0,sampling.x==0); let wrap=vec2i(sampling.yz);
    if sampling.x!=2 { return sampleLevel(image,uv,u32(floor(lod+0.5)),linear,wrap); }
    let low=u32(floor(lod)); return mix(sampleLevel(image,uv,low,linear,wrap),sampleLevel(image,uv,min(low+1,levels-1),linear,wrap),fract(lod));
}
fn safeNormal(v: vec3f) -> vec3f { return v*inverseSqrt(max(dot(v,v),1e-20)); }
@fragment fn fragment(i: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let base=parameter(12)*i.color*sampleMaterial(baseImage,0,i.uv01.xy);
    let mr=sampleMaterial(mrImage,1,i.uv01.zw); let emissive=sampleMaterial(emissiveImage,4,i.uv4);
    let emission=parameter(13); let material=parameter(14); let camera=parameter(15);
    if material.z==1 && base.a<material.w { discard; }
    let alpha=select(1.0,base.a,material.z==2);
    var radiance=emission.xyz*emissive.rgb;
    if emission.w>0 { radiance=base.rgb; }
    else {
        let n=safeNormal(i.normal)*select(-1.0,1.0,front); let v=select(safeNormal(camera.xyz-i.world),parameter(16).xyz,parameter(16).w>0);
        let nv=max(dot(n,v),0.0); let metallic=material.x*mr.b;
        let roughness=max(material.y*mr.g,0.045); let a2=pow(roughness,4.0);
        let f0=mix(vec3f(0.04),base.rgb,metallic);
        for(var index=0u;index<u32(camera.w);index++) {
            let position=parameter(37+index*4); let direction=parameter(38+index*4); let color=parameter(39+index*4);
            let delta=position.xyz-i.world; let d2=max(dot(delta,delta),1e-6);
            let l=select(delta*inverseSqrt(d2),-direction.xyz,position.w==0);
            var attenuation=select(1.0/d2,1.0,position.w==0);
            if position.w!=0 && direction.w>0 { attenuation*=clamp(1.0-pow(sqrt(d2)/direction.w,4.0),0.0,1.0); }
            if position.w==2 {
                let outer=parameter(40+index*4).x;
                let spot=clamp((dot(-l,direction.xyz)-outer)/max(color.w-outer,1e-6),0.0,1.0); attenuation*=spot*spot;
            }
            let nl=max(dot(n,l),0.0); let h=safeNormal(v+l);
            let nh=max(dot(n,h),0.0); let vh=max(dot(v,h),0.0);
            let den=nh*nh*(a2-1.0)+1.0;
            let distribution=a2/(3.14159265359*den*den);
            let visibility=0.5/max(nl*sqrt(nv*nv*(1.0-a2)+a2)+nv*sqrt(nl*nl*(1.0-a2)+a2),1e-6);
            let fresnel=f0+(vec3f(1)-f0)*pow(1.0-vh,5.0);
            let diffuse=(vec3f(1)-fresnel)*(1.0-metallic)*base.rgb/3.14159265359;
            radiance+=(diffuse+fresnel*distribution*visibility)*color.rgb*(nl*attenuation);
        }
    }
    return vec4f(radiance*alpha,alpha);
}
