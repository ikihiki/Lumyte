requires immediate_address_space;
struct Root { offset: u32, reserved0: u32, reserved1: u32, reserved2: u32 }
var<immediate> root: Root;
@group(0) @binding(0) var<storage, read> scene: array<vec4f>;
@group(0) @binding(1) var backdrop: texture_2d<f32>;
@group(0) @binding(2) var image: texture_2d<f32>;
@group(0) @binding(3) var maskImage: texture_2d<f32>;

fn field(i: u32) -> vec4f { return scene[root.offset + i]; }
fn segmentDistance(p: vec2f, a: vec2f, b: vec2f) -> f32 {
    let d = b - a;
    return length(p - (a + d * clamp(dot(p - a, d) / max(dot(d,d), 1e-20), 0.0, 1.0)));
}
fn insideShape(p: vec2f, start: u32, count: u32, rule: u32) -> bool {
    var winding = 0i;
    for (var i = 0u; i < count; i++) {
        let edge = scene[start + i]; let a = edge.xy; let b = edge.zw;
        if ((a.y <= p.y && b.y > p.y) || (b.y <= p.y && a.y > p.y)) {
            let x = a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y);
            if (x > p.x) { winding += select(-1i, 1i, b.y > a.y); }
        }
    }
    return select(winding != 0, (abs(winding) % 2) != 0, rule == 1u);
}
fn boundaryDistance(p: vec2f, start: u32, count: u32) -> f32 {
    var distance = 1e20;
    for (var i = 0u; i < count; i++) {
        let edge = scene[start + i]; distance = min(distance, segmentDistance(p, edge.xy, edge.zw));
    }
    return distance;
}
fn insideDrawing(p: vec2f) -> bool {
    let shape=field(0u);
    if(field(11u).x==0.0 && shape.w<0.5 && !insideShape(p,u32(shape.x),u32(shape.y),u32(shape.z))) { return false; }
    let clip=field(12u);
    for(var i=0u;i<u32(clip.y);i++) {
        let c=scene[u32(clip.x)+i]; if(!insideShape(p,u32(c.x),u32(c.y),u32(c.z))) { return false; }
    }
    return true;
}
fn drawingCoverage(p: vec2f) -> f32 {
    let shape=field(0u); var distance=1e20;
    if(field(11u).x==0.0 && shape.w<0.5) { distance=boundaryDistance(p,u32(shape.x),u32(shape.y)); }
    let clip=field(12u);
    for(var i=0u;i<u32(clip.y);i++) { let c=scene[u32(clip.x)+i]; distance=min(distance,boundaryDistance(p,u32(c.x),u32(c.y))); }
    if(distance>=0.707107) { return select(0.0,1.0,insideDrawing(p)); }
    var covered = 0.0;
    for (var y = 0u; y < 8u; y++) { for (var x = 0u; x < 8u; x++) {
        let samplePoint=p-vec2f(0.5)+(vec2f(f32(x),f32(y))+vec2f(0.5))/8.0;
        covered += select(0.0,1.0,insideDrawing(samplePoint));
    } }
    return covered/64.0;
}
fn extend(t: f32, mode: u32) -> f32 {
    if (mode == 1u) { return fract(t); }
    if (mode == 2u) { let x = t - floor(t * 0.5) * 2.0; return select(x, 2.0 - x, x > 1.0); }
    return clamp(t, 0.0, 1.0);
}
fn gradient(t0: f32) -> vec4f {
    let info = field(1u); let n = u32(info.z); let start = u32(info.y);
    if (n == 0u) { return field(4u); }
    let t = extend(t0, u32(info.w));
    var previousPosition = scene[start].x; var previous = scene[start + 1u];
    if (t < previousPosition) { return previous; }
    for (var i = 1u; i < n; i++) {
        let position = scene[start + i * 2u].x; let color = scene[start + i * 2u + 1u];
        if (t < position) { return mix(previous, color, clamp((t - previousPosition) / max(position - previousPosition, 1e-20), 0.0, 1.0)); }
        previousPosition = position; previous = color;
    }
    return previous;
}
fn decoded(color: vec4f) -> vec4f {
    let settings = field(6u); var a = color.a; var rgb = color.rgb;
    if (u32(settings.w) == 0u) { a = 1.0; }
    if (u32(settings.w) == 2u && a > 0.0) { rgb /= a; }
    if (u32(settings.z) == 1u) { rgb = select(pow((rgb + vec3f(0.055)) / 1.055, vec3f(2.4)), rgb / 12.92, rgb <= vec3f(0.04045)); }
    return vec4f(rgb * a, a);
}
fn imageCoordinate(value: i32, low: i32, high: i32, mode: u32) -> i32 {
    let count=max(high-low+1,1);
    if(mode==1u){return low+((value-low)%count+count)%count;}
    if(mode==2u){let period=count*2; let p=((value-low)%period+period)%period; return low+select(p,period-1-p,p>=count);}
    return clamp(value,low,high);
}
fn imagePoint(point: vec2i) -> vec4f {
    let size = vec2i(textureDimensions(image));
    let low=vec2i(0); let high=size-1;
    let modes=field(9u); let wrapped=vec2i(imageCoordinate(point.x,low.x,high.x,u32(modes.y)),imageCoordinate(point.y,low.y,high.y,u32(modes.z)));
    return decoded(textureLoad(image, clamp(wrapped, vec2i(0), size - 1), 0));
}
fn imageSample(point: vec2f) -> vec4f {
    if (field(9u).x < 0.5) { return imagePoint(vec2i(floor(point))); }
    let p = point - 0.5; let low = vec2i(floor(p)); let f = fract(p);
    return mix(mix(imagePoint(low), imagePoint(low + vec2i(1,0)), f.x),
        mix(imagePoint(low + vec2i(0,1)), imagePoint(low + vec2i(1,1)), f.x), f.y);
}
fn localPoint(point: vec2f) -> vec2f {
    let transform = field(5u);
    return vec2f(point.x * transform.x + point.y * transform.z, point.x * transform.y + point.y * transform.w) + field(6u).xy;
}
fn paint(point: vec2f) -> vec4f {
    let p = localPoint(point); let kind = u32(field(1u).x); let a = field(2u); let b = field(3u);
    if (kind == 0u) { return field(4u); }
    if (kind == 1u) { let d = a.zw - a.xy; let projection=b.xy-a.xy; let q=p-a.xy;
        let determinant=d.x*projection.y-d.y*projection.x;
        return gradient((q.x*projection.y-q.y*projection.x)/select(1e-20,determinant,abs(determinant)>1e-20)); }
    if (kind == 2u) {
        let delta = a.zw - a.xy; let q = p - a.xy; let dr = b.w - b.z;
        let aa = dot(delta,delta) - dr * dr;
        let bb = -2.0 * (dot(q,delta) + b.z * dr); let cc = dot(q,q) - b.z*b.z;
        var t = 0.0;
        if (abs(aa) < 1e-10) { t = -cc / select(1e-10, bb, abs(bb) > 1e-10); }
        else { let disc = bb*bb - 4.0*aa*cc; if (disc < 0.0) { return vec4f(0); }
            let sq = sqrt(disc); let t0 = (-bb-sq)/(2.0*aa); let t1 = (-bb+sq)/(2.0*aa);
            t = max(t0,t1); if (b.z + t*dr < 0.0) { t = min(t0,t1); }
            if (b.z + t*dr < 0.0) { return vec4f(0); }
        }
        return gradient(t);
    }
    if (kind == 3u) {
        var angle = atan2(p.y-a.y,p.x-a.x); let twoPi = 6.283185307179586;
        angle = angle - floor((angle - b.x) / twoPi) * twoPi;
        return gradient((angle - b.x) / max(b.y-b.x, 1e-20));
    }
    let source = field(7u); let destination = field(8u);
    let uv = (p - destination.xy) / destination.zw;
    let samplePoint = source.xy + uv * source.zw;
    if (kind == 4u) { return imageSample(samplePoint) * field(4u); }
    let raw = textureLoad(image, clamp(vec2i(samplePoint), vec2i(0), vec2i(textureDimensions(image))-1), 0);
    var d = raw.r; let df = field(10u);
    if (df.x > 1.5) { d = max(min(raw.r,raw.g),min(max(raw.r,raw.g),raw.b)); }
    var alpha = d;
    if (df.x > 0.5) { alpha = clamp((d - 0.5) * df.y * df.z + 0.5,0.0,1.0); }
    return field(4u) * alpha;
}
fn luminosity(c: vec3f) -> f32 { return dot(c, vec3f(0.3,0.59,0.11)); }
fn saturation(c: vec3f) -> f32 { return max(c.r,max(c.g,c.b)) - min(c.r,min(c.g,c.b)); }
fn clipColor(c0: vec3f) -> vec3f {
    var c = c0; let l = luminosity(c); let n = min(c.r,min(c.g,c.b)); let x = max(c.r,max(c.g,c.b));
    if (n < 0.0) { c = vec3f(l) + (c - l) * l / (l - n); }
    if (x > 1.0) { c = vec3f(l) + (c - l) * (1.0 - l) / (x - l); }
    return c;
}
fn setLum(c: vec3f, l: f32) -> vec3f { return clipColor(c + (l-luminosity(c))); }
fn setSat(c: vec3f, s: f32) -> vec3f {
    let n = min(c.r,min(c.g,c.b)); let x = max(c.r,max(c.g,c.b));
    if (x <= n) { return vec3f(0); } return (c-n)*s/(x-n);
}
fn blendComponent(mode: u32,b: f32,s: f32) -> f32 {
    if (mode == 14u) { return select(1.0-2.0*(1.0-b)*(1.0-s),2.0*b*s,b<=0.5); }
    if (mode == 15u) { return min(b,s); } if (mode == 16u) { return max(b,s); }
    if (mode == 17u) { if(b<=0.0){return 0.0;} if(s>=1.0){return 1.0;} return min(1.0,b/(1.0-s)); }
    if (mode == 18u) { if(b>=1.0){return 1.0;} if(s<=0.0){return 0.0;} return 1.0-min(1.0,(1.0-b)/s); }
    if (mode == 19u) { return select(1.0-2.0*(1.0-b)*(1.0-s),2.0*b*s,s<=0.5); }
    if (mode == 20u) { var d = sqrt(b); if (b<=0.25) { d=((16.0*b-12.0)*b+4.0)*b; }
        return select(b+(2.0*s-1.0)*(d-b),b-(1.0-2.0*s)*b*(1.0-b),s<=0.5); }
    if (mode == 21u) { return abs(b-s); } if (mode == 22u) { return b+s-2.0*b*s; }
    return b*s;
}
fn composite(mode: u32,s: vec4f,b: vec4f) -> vec4f {
    if(mode==0u){return vec4f(0);} if(mode==1u){return s;} if(mode==2u){return b;}
    if(mode==3u){return s+b*(1.0-s.a);} if(mode==4u){return s*(1.0-b.a)+b;}
    if(mode==5u){return s*b.a;} if(mode==6u){return b*s.a;}
    if(mode==7u){return s*(1.0-b.a);} if(mode==8u){return b*(1.0-s.a);}
    if(mode==9u){return s*b.a+b*(1.0-s.a);} if(mode==10u){return s*(1.0-b.a)+b*s.a;}
    if(mode==11u){return s*(1.0-b.a)+b*(1.0-s.a);} if(mode==12u){return min(vec4f(1),s+b);}
    let alpha=s.a+b.a-s.a*b.a; if(mode==13u){return vec4f(s.rgb+b.rgb-s.rgb*b.rgb,alpha);}
    var sc=vec3f(0); var bc=vec3f(0); if(s.a>0.0){sc=s.rgb/s.a;} if(b.a>0.0){bc=b.rgb/b.a;}
    var blended=vec3f(0);
    if(mode<=23u){blended=vec3f(blendComponent(mode,bc.r,sc.r),blendComponent(mode,bc.g,sc.g),blendComponent(mode,bc.b,sc.b));}
    else if(mode==24u){blended=setLum(setSat(sc,saturation(bc)),luminosity(bc));}
    else if(mode==25u){blended=setLum(setSat(bc,saturation(sc)),luminosity(bc));}
    else if(mode==26u){blended=setLum(sc,luminosity(bc));} else {blended=setLum(bc,luminosity(sc));}
    return vec4f(s.rgb*(1.0-b.a)+b.rgb*(1.0-s.a)+blended*s.a*b.a,alpha);
}
fn sourcePoint(p: vec2i) -> vec4f {
    let size = vec2i(textureDimensions(image));
    if(any(p < vec2i(0)) || any(p >= size)) { return vec4f(0); }
    return textureLoad(image,p,0);
}
fn sourceSample(point: vec2f) -> vec4f {
    let p=point-0.5; let low=vec2i(floor(p)); let f=fract(p);
    return mix(mix(sourcePoint(low),sourcePoint(low+vec2i(1,0)),f.x),mix(sourcePoint(low+vec2i(0,1)),sourcePoint(low+vec2i(1,1)),f.x),f.y);
}
fn distancePoint(point: vec2i) -> vec4f { return textureLoad(maskImage,clamp(point,vec2i(0),vec2i(textureDimensions(maskImage))-1),0); }
fn distanceSample(point: vec2f) -> vec4f {
    let p=point-0.5; let low=vec2i(floor(p)); let f=fract(p);
    return mix(mix(distancePoint(low),distancePoint(low+vec2i(1,0)),f.x),mix(distancePoint(low+vec2i(0,1)),distancePoint(low+vec2i(1,1)),f.x),f.y);
}
@vertex fn vertex(@builtin(vertex_index) id: u32) -> @builtin(position) vec4f {
    let vertices = array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3));
    return vec4f(vertices[id],0,1);
}
@fragment fn fragment(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let p=position.xy; let info=field(11u); let operation=u32(info.x);
    let old=textureLoad(backdrop,vec2i(p),0);
    if(operation==2u) {
        let radius=field(2u).x; let direction=vec2i(field(2u).zw);
        let extent=i32(ceil(radius*3.0)); var sum=vec4f(0); var weight=0.0;
        for(var i=-extent;i<=extent;i++) { let x=f32(i); let w=exp(-0.5*x*x/max(radius*radius,0.001));
            sum+=sourcePoint(vec2i(p)+direction*i)*w; weight+=w; }
        return sum/max(weight,1e-20);
    }
    var alpha=drawingCoverage(p); var color=vec4f(0);
    if(operation==0u) { color=paint(p); }
    else {
        color=sourceSample(p-field(2u).xy);
        if(operation==3u){color=field(4u)*color.a;}
        if(field(12u).z>0.5){color*=textureLoad(maskImage,vec2i(p),0).a;}
        color*=info.z;
    }
    color*=field(13u).x;
    if (operation==0u && field(9u).w>0.5) {
        let matrixIndex=u32(field(15u).x); let m=scene[matrixIndex];
        let q=vec2f(p.x*m.x+p.y*m.z,p.x*m.y+p.y*m.w)+scene[matrixIndex+1u].xy;
        let rect=field(14u); let samplePoint=(q-rect.xy)/rect.zw*vec2f(textureDimensions(maskImage));
        let raw=distanceSample(samplePoint);
        let df=field(10u); var d=raw.r;
        if(df.x>1.5){d=max(min(raw.r,raw.g),min(max(raw.r,raw.g),raw.b));}
        alpha*=select(d,clamp((d-0.5)*df.y*df.z+0.5,0.0,1.0),df.x>0.5);
    }
    return mix(old,composite(u32(info.y),color,old),alpha);
}
