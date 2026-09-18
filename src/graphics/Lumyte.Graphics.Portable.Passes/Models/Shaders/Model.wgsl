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
@group(0) @binding(7) var environmentDiffuse: texture_2d<f32>;
@group(0) @binding(8) var environmentSpecular: texture_2d<f32>;
@group(0) @binding(9) var environmentBrdf: texture_2d<f32>;
fn parameter(i: u32) -> vec4f { return parameters[root.offset+i]; }
fn transform(v: vec4f, offset: u32) -> vec4f { return v.x*parameter(offset)+v.y*parameter(offset+1)+v.z*parameter(offset+2)+v.w*parameter(offset+3); }
struct VertexOutput { @builtin(position) position: vec4f, @location(0) world: vec3f, @location(1) normal: vec3f, @location(2) color: vec4f,
    @location(3) uv01: vec4f, @location(4) uv23: vec4f, @location(5) uv4: vec2f, @location(6) tangent: vec4f }
@vertex fn vertex(@builtin(vertex_index) id: u32) -> VertexOutput {
    let world=transform(geometry[id*7],0);
    return VertexOutput(transform(world,4),world.xyz,transform(geometry[id*7+1],8).xyz,geometry[id*7+2],geometry[id*7+3],geometry[id*7+4],geometry[id*7+5].xy,
        vec4f(transform(vec4f(geometry[id*7+6].xyz,0),0).xyz,geometry[id*7+6].w*parameter(28).w));
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
fn environmentDirection(v:vec3f)->vec3f {
    return v.x*parameter(38).xyz+v.y*parameter(39).xyz+v.z*parameter(40).xyz;
}
fn environmentUv(direction:vec3f)->vec2f {
    return vec2f(atan2(direction.z,direction.x)/(2*3.141592653589793)+0.5,acos(clamp(direction.y,-1.0,1.0))/3.141592653589793);
}
fn atlasTexel(p:vec2i,size:vec2i,layer:u32)->vec4f {
    return textureLoad(environmentSpecular,vec2i(wrapTexel(p.x,size.x,2),clamp(p.y,0,size.y-1)+i32(layer)*size.y),0);
}
fn sampleEnvironmentLayer(uv:vec2f,layer:u32)->vec3f {
    let dimensions=textureDimensions(environmentSpecular);let size=vec2i(vec2u(dimensions.x,dimensions.y/8));
    let p=uv*vec2f(size)-0.5;let q=vec2i(floor(p));let t=fract(p);
    return mix(mix(atlasTexel(q,size,layer),atlasTexel(q+vec2i(1,0),size,layer),t.x),
        mix(atlasTexel(q+vec2i(0,1),size,layer),atlasTexel(q+vec2i(1),size,layer),t.x),t.y).rgb;
}
fn environmentLighting(n:vec3f,v:vec3f,nv:f32,roughness:f32,metallic:f32,base:vec3f,f0:vec3f,occlusion:f32)->vec3f {
    let irradiance=sampleLevel(environmentDiffuse,environmentUv(environmentDirection(n)),0,true,vec2i(2,0)).rgb;
    let uv=environmentUv(environmentDirection(reflect(-v,n)));
    let layer=clamp(roughness,0.0,1.0)*7;let low=u32(floor(layer));
    let reflection=mix(sampleEnvironmentLayer(uv,low),sampleEnvironmentLayer(uv,min(low+1,7)),fract(layer));
    let integration=sampleLevel(environmentBrdf,vec2f(nv,roughness),0,true,vec2i(0,0)).xy;
    return ((1-f0)*(1-metallic)*base*irradiance+reflection*(f0*integration.x+integration.y))*parameter(37).w*occlusion;
}

fn safeNormal(v: vec3f) -> vec3f { return v*inverseSqrt(max(dot(v,v),1e-20)); }
@fragment fn fragment(i: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let base=parameter(12)*i.color*sampleMaterial(baseImage,0,i.uv01.xy);
    let occlusion=sampleMaterial(occlusionImage,3,i.uv23.zw);
    let normalMap=sampleMaterial(normalImage,2,i.uv23.xy);
    let mr=sampleMaterial(mrImage,1,i.uv01.zw); let emissive=sampleMaterial(emissiveImage,4,i.uv4);
    let emission=parameter(13); let material=parameter(14); let camera=parameter(15);
    if material.z==1 && base.a<material.w { discard; }
    let alpha=select(1.0,base.a,material.z==2);
    var radiance=emission.xyz*emissive.rgb;
    if emission.w>0 { radiance=base.rgb; }
    else {
        var n=safeNormal(i.normal);
        if parameter(25).y>0 {
            let t=safeNormal(i.tangent.xyz-n*dot(n,i.tangent.xyz)); let b=cross(n,t)*i.tangent.w;
            var local=normalMap.xyz*2-1; local=vec3f(local.xy*parameter(28).z,local.z);
            n=safeNormal(t*local.x+b*local.y+n*local.z);
        }
        n*=select(-1.0,1.0,front); let v=select(safeNormal(camera.xyz-i.world),parameter(16).xyz,parameter(16).w>0);
        let nv=max(dot(n,v),0.0); let metallic=material.x*mr.b;
        let roughness=max(material.y*mr.g,0.045); let a2=pow(roughness,4.0);
        let f0=mix(vec3f(0.04),base.rgb,metallic);
        if parameter(41).x>0 { radiance+=environmentLighting(n,v,nv,roughness,metallic,base.rgb,f0,mix(1.0,occlusion.r,parameter(41).z)); }
        for(var index=0u;index<u32(camera.w);index++) {
            let position=parameter(42+index*4); let direction=parameter(43+index*4); let color=parameter(44+index*4);
            let delta=position.xyz-i.world; let d2=max(dot(delta,delta),1e-6);
            let l=select(delta*inverseSqrt(d2),-direction.xyz,position.w==0);
            var attenuation=select(1.0/d2,1.0,position.w==0);
            if position.w!=0 && direction.w>0 { attenuation*=clamp(1.0-pow(sqrt(d2)/direction.w,4.0),0.0,1.0); }
            if position.w==2 {
                let outer=parameter(45+index*4).x;
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
