import shared_math;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
uint4 getRoot()
{
    __requirePrelude("var<immediate> lumyteRoot: vec4<u32>;");
    __intrinsic_asm "(lumyteRoot)";
}
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    uint4 root = getRoot();
    output[0] = evaluateRoot(root.x, root.y);
}
