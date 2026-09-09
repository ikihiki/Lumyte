struct RootData
{
    uint count;
    float scale;
    float3 vector;
    float bias;
};
[[vk::push_constant]] ConstantBuffer<RootData> nativeRoot;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
uint getImmediateCount()
{
    __requirePrelude("requires immediate_address_space;\nstruct LumyteRoot { count: u32, scale: f32, vector: vec3<f32>, bias: f32, };\nvar<immediate> lumyteRoot: LumyteRoot;");
    __intrinsic_asm "(lumyteRoot.count)";
}
float getImmediateScale()
{
    __requirePrelude("requires immediate_address_space;\nstruct LumyteRoot { count: u32, scale: f32, vector: vec3<f32>, bias: f32, };\nvar<immediate> lumyteRoot: LumyteRoot;");
    __intrinsic_asm "(lumyteRoot.scale)";
}
float3 getImmediateVector()
{
    __requirePrelude("requires immediate_address_space;\nstruct LumyteRoot { count: u32, scale: f32, vector: vec3<f32>, bias: f32, };\nvar<immediate> lumyteRoot: LumyteRoot;");
    __intrinsic_asm "(lumyteRoot.vector)";
}
float getImmediateBias()
{
    __requirePrelude("requires immediate_address_space;\nstruct LumyteRoot { count: u32, scale: f32, vector: vec3<f32>, bias: f32, };\nvar<immediate> lumyteRoot: LumyteRoot;");
    __intrinsic_asm "(lumyteRoot.bias)";
}
RootData getRoot()
{
    __target_switch
    {
    case wgsl:
        return RootData(getImmediateCount(), getImmediateScale(), getImmediateVector(), getImmediateBias());
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
