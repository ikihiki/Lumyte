@binding(0) @group(0) var<storage, read_write> values_0 : array<u32>;

@compute
@workgroup_size(64, 1, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : vec2<u32> = vec2<u32>(arrayLength(&values_0), 4);
    var _S2 : u32 = id_0.x;
    if(_S2 < (_S1.x))
    {
        values_0[_S2] = values_0[_S2] * u32(2);
    }
    return;
}
