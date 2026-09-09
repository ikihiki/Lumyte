requires immediate_address_space;
var<immediate> root: vec4<f32>;
@vertex
fn vertexMain(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
    let corners = array<vec2<f32>, 3>(vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));
    return vec4<f32>(corners[index], 0.0, root.w);
}
@fragment
fn fragmentMain() -> @location(0) vec4<f32> {
    return root;
}
