requires immediate_address_space;
struct RootData {
    count: u32,
    gain: f32,
    offset: vec3<f32>,
    index: u32,
}
var<immediate> root: RootData;
@group(0) @binding(0) var<storage, read_write> output: array<vec4<f32>>;
@compute @workgroup_size(1)
fn main() {
    output[root.index] = vec4<f32>(f32(root.count) * root.gain + root.offset.x,
                                  root.offset.y, root.offset.z, f32(root.index));
}
