requires immediate_address_space;
struct Root { mode:u32,width:u32,height:u32 }
var<immediate> root:Root;
@group(0) @binding(0) var image:texture_2d<f32>;
const pi=3.141592653589793;
@vertex fn vertex(@builtin(vertex_index) id:u32)->@builtin(position) vec4f {
    let vertices=array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3));return vec4f(vertices[id],0,1);
}
fn direction(uv:vec2f)->vec3f {
    let theta=uv.y*pi;let phi=(uv.x-0.5)*2*pi;
    return vec3f(sin(theta)*cos(phi),cos(theta),sin(theta)*sin(phi));
}
fn envTexel(p:vec2i,size:vec2i)->vec4f {
    return textureLoad(image,vec2i(((p.x%size.x)+size.x)%size.x,clamp(p.y,0,size.y-1)),0);
}
fn radiance(dir:vec3f)->vec3f {
    let size=vec2i(textureDimensions(image));
    let uv=vec2f(atan2(dir.z,dir.x)/(2*pi)+0.5,acos(clamp(dir.y,-1.0,1.0))/pi);
    let p=uv*vec2f(size)-0.5;let q=vec2i(floor(p));let t=fract(p);
    return mix(mix(envTexel(q,size),envTexel(q+vec2i(1,0),size),t.x),
        mix(envTexel(q+vec2i(0,1),size),envTexel(q+vec2i(1),size),t.x),t.y).rgb;
}
fn sequence(index:u32,count:u32)->vec2f {
    return vec2f(f32(index)/f32(count),f32(reverseBits(index))*2.3283064365386963e-10+0.5/f32(count));
}
fn hemisphere(n:vec3f,xi:vec2f,cosine:f32)->vec3f {
    let sine=sqrt(max(0.0,1-cosine*cosine));let phi=2*pi*xi.x;
    let up=select(vec3f(1,0,0),vec3f(0,0,1),abs(n.z)<0.999);
    let t=normalize(cross(up,n));let b=cross(n,t);
    return t*(sine*cos(phi))+b*(sine*sin(phi))+n*cosine;
}
@fragment fn fragment(@builtin(position) position:vec4f)->@location(0) vec4f {
    if root.mode==2 {
        let nv=position.x/f32(root.width);let roughness=position.y/f32(root.height);let a2=pow(roughness,4.0);
        let v=vec3f(sqrt(max(0.0,1-nv*nv)),0,nv);var sum=vec2f(0);
        for(var i=0u;i<512u;i++) {
            let xi=sequence(i,512);let nh=sqrt((1-xi.y)/(1+(a2-1)*xi.y));
            let h=hemisphere(vec3f(0,0,1),xi,nh);let vh=max(dot(v,h),0.0);
            let l=2*vh*h-v;let nl=max(l.z,0.0);
            if nl>0 {
                let visibility=0.5/max(nl*sqrt(nv*nv*(1-a2)+a2)+nv*sqrt(nl*nl*(1-a2)+a2),1e-6);
                let weight=4*visibility*nl*vh/max(nh,1e-6);let fc=pow(1-vh,5.0);
                sum+=vec2f(1-fc,fc)*weight;
            }
        }
        return vec4f(sum/512,0,1);
    }
    var uv=position.xy/vec2f(f32(root.width),f32(root.height));var roughness=0.0;
    if root.mode==1 {
        let layerHeight=f32(root.height)/8;let layer=u32(position.y/layerHeight);
        uv.y=fract(position.y/layerHeight);roughness=f32(layer)/7;
    }
    let n=direction(uv);
    if root.mode==1 && roughness==0 { return vec4f(radiance(n),1); }
    var sum=vec3f(0);var weight=0.0;let a2=pow(roughness,4.0);
    for(var i=0u;i<128u;i++) {
        let xi=sequence(i,128);
        if root.mode==0 { sum+=radiance(hemisphere(n,xi,sqrt(1-xi.y)));weight+=1; }
        else {
            let nh=sqrt((1-xi.y)/(1+(a2-1)*xi.y));let h=hemisphere(n,xi,nh);
            let l=2*dot(n,h)*h-n;let nl=max(dot(n,l),0.0);
            if nl>0 { sum+=radiance(l)*nl;weight+=nl; }
        }
    }
    return vec4f(sum/max(weight,1e-6),1);
}
