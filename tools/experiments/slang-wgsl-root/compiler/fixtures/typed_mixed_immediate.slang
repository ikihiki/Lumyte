__target_intrinsic(wgsl, "LumyteRoot")
struct RootData
{
    uint count;
    float scale;
    float3 vector;
    float bias;
};
[[vk::push_constant]] ConstantBuffer<RootData> nativeRoot;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
RootData getRoot()
{
    __target_switch
    {
    case wgsl:
        __requirePrelude("requires immediate_address_space;\nstruct LumyteRoot { count: u32, scale: f32, vector: vec3<f32>, bias: f32, };\nvar<immediate> lumyteRoot: LumyteRoot;");
        __intrinsic_asm "(lumyteRoot)";
    default:
        return nativeRoot;
    }
}
uint evaluateMixed(RootData root)
{
    return root.count + uint(root.scale * (root.vector.x + root.vector.y + root.vector.z) + root.bias);
}
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    output[0] = evaluateMixed(getRoot());
}
