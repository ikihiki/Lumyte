requires immediate_address_space;
struct Root { offset: u32, reserved: u32 }
var<immediate> root: Root;
@group(0) @binding(0) var<storage,read> geometry: array<vec4f>;
@group(0) @binding(1) var<storage,read> parameters: array<vec4f>;
fn parameter(i: u32) -> vec4f { return parameters[root.offset+i]; }
fn transform(v: vec4f, offset: u32) -> vec4f { return v.x*parameter(offset)+v.y*parameter(offset+1)+v.z*parameter(offset+2)+v.w*parameter(offset+3); }
struct VertexOutput { @builtin(position) position: vec4f, @location(0) world: vec3f, @location(1) normal: vec3f, @location(2) color: vec4f }
@vertex fn vertex(@builtin(vertex_index) id: u32) -> VertexOutput {
    let world=transform(geometry[id*3],0);
    return VertexOutput(transform(world,4),world.xyz,transform(geometry[id*3+1],8).xyz,geometry[id*3+2]);
}
fn safeNormal(v: vec3f) -> vec3f { return v*inverseSqrt(max(dot(v,v),1e-20)); }
@fragment fn fragment(i: VertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    let base=parameter(12)*i.color;
    let emission=parameter(13); let material=parameter(14); let camera=parameter(15);
    if material.z==1 && base.a<material.w { discard; }
    let alpha=select(1.0,base.a,material.z==2);
    var radiance=emission.xyz;
    if emission.w>0 { radiance=base.rgb; }
    else {
        let n=safeNormal(i.normal)*select(-1.0,1.0,front); let v=select(safeNormal(camera.xyz-i.world),parameter(16).xyz,parameter(16).w>0);
        let nv=max(dot(n,v),0.0); let metallic=material.x;
        let roughness=max(material.y,0.045); let a2=pow(roughness,4.0);
        let f0=mix(vec3f(0.04),base.rgb,metallic);
        for(var index=0u;index<u32(camera.w);index++) {
            let position=parameter(17+index*4); let direction=parameter(18+index*4); let color=parameter(19+index*4);
            let delta=position.xyz-i.world; let d2=max(dot(delta,delta),1e-6);
            let l=select(delta*inverseSqrt(d2),-direction.xyz,position.w==0);
            var attenuation=select(1.0/d2,1.0,position.w==0);
            if position.w!=0 && direction.w>0 { attenuation*=clamp(1.0-pow(sqrt(d2)/direction.w,4.0),0.0,1.0); }
            if position.w==2 {
                let outer=parameter(20+index*4).x;
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
