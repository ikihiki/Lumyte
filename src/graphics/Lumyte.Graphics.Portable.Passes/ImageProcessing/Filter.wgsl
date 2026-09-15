requires immediate_address_space;
struct Root { mode: u32, parameter: u32, width: u32, height: u32 }
var<immediate> root: Root;
@group(0) @binding(0) var image: texture_2d<f32>;
@vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
    let vertices = array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3));
    return vec4f(vertices[id],0,1);
}
fn readPixel(p: vec2i, size: vec2i) -> vec4f { return textureLoad(image, clamp(p,vec2i(0),size-1),0); }
@fragment fn fragment(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let size = vec2i(textureDimensions(image));
    let p = vec2i(position.xy);
    if (root.mode <= 1) {
        var coordinate = position.xy * vec2f(size) / vec2f(f32(root.width),f32(root.height));
        if (root.mode == 0) { return readPixel(vec2i(floor(coordinate)),size); }
        coordinate -= 0.5;
        let lo = vec2i(floor(coordinate)); let t = fract(coordinate);
        return mix(mix(readPixel(lo,size),readPixel(lo+vec2i(1,0),size),t.x),
                   mix(readPixel(lo+vec2i(0,1),size),readPixel(lo+1,size),t.x),t.y);
    }
    if (root.mode <= 3) {
        let axis = select(p.y,p.x,root.mode==2);
        let extent = select(size.y,size.x,root.mode==2);
        let radius = i32(root.parameter);
        let first = axis-min(radius,axis);
        let last = axis+min(radius,extent-1-axis);
        let denominator = f32(radius)*2+1;
        var value = vec4f(0);
        for (var i=first; i<=last; i++) {
            var count = 1.0;
            if (i==0) { count += f32(max(0,radius-axis)); }
            if (i==extent-1) { count += f32(max(0,radius-(extent-1-axis))); }
            value += readPixel(select(vec2i(p.x,i),vec2i(i,p.y),root.mode==2),size)*(count/denominator);
        }
        return value;
    }
    let value = readPixel(p,size);
    if (root.mode==4) {
        if (value.a<=0) { return vec4f(0); }
        let straight = max(value.rgb/value.a,vec3f(0));
        let mapped = 1.0/(1.0+exp2(-bitcast<f32>(root.parameter)-log2(straight)));
        return vec4f(select(vec3f(0),mapped,straight>vec3f(0))*value.a,value.a);
    }
    return value*bitcast<f32>(root.parameter);
}
