@group(0) @binding(0) var image: texture_2d<f32>;
@group(0) @binding(1) var imageSampler: sampler;
@group(1) @binding(4) var shadow: texture_depth_2d;
@group(1) @binding(5) var shadowSampler: sampler_comparison;
@group(2) @binding(2) var destination: texture_storage_2d<rgba8unorm, write>;
@compute @workgroup_size(1)
fn main() {
  let value = textureSampleLevel(image, imageSampler, vec2<f32>(0.5), 0.0);
  let comparison = textureSampleCompareLevel(shadow, shadowSampler, vec2<f32>(0.5), 0.5);
  textureStore(destination, vec2<i32>(0), value * comparison);
}
