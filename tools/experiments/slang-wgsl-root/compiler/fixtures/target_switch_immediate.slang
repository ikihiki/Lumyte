import shared_math;
[[vk::push_constant]] ConstantBuffer<uint4> nativeRoot;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
uint4 getRoot()
{
    __target_switch
    {
    case wgsl:
        __requirePrelude("requires immediate_address_space;\nvar<immediate> lumyteRoot: vec4<u32>;");
        __intrinsic_asm "(lumyteRoot)";
    default:
        return nativeRoot;
    }
}
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    uint4 root = getRoot();
    output[0] = evaluateRoot(root.x, root.y);
}
