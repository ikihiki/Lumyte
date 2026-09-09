import shared_math;
struct RootData { uint value; uint bias; };
uniform RootData root;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    output[0] = evaluateRoot(root.value, root.bias);
}
