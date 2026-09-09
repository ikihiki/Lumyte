requires immediate_address_space;

struct RootData {
    value: u32,
    bias: u32,
}

var<immediate> root: RootData;
@group(0) @binding(0) var<storage, read_write> output: array<u32>;

@compute @workgroup_size(1)
fn main() {
    output[0] = root.value * 3u + root.bias;
}
