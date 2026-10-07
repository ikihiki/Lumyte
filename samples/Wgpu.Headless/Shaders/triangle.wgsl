struct vertexOutput_0
{
    @builtin(position) output_0 : vec4<f32>,
};

@vertex
fn vertexMain(@builtin(vertex_index) id_0 : u32) -> vertexOutput_0
{
    var positions_0 : array<vec2<f32>, i32(3)> = array<vec2<f32>, i32(3)>( vec2<f32>(-0.75f, -0.75f), vec2<f32>(0.75f, -0.75f), vec2<f32>(0.0f, 0.75f) );
    var _S1 : vertexOutput_0 = vertexOutput_0( vec4<f32>(positions_0[id_0], 0.0f, 1.0f) );
    return _S1;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

@fragment
fn fragmentMain() -> pixelOutput_0
{
    var _S2 : pixelOutput_0 = pixelOutput_0( vec4<f32>(1.0f, 0.0f, 0.0f, 1.0f) );
    return _S2;
}
