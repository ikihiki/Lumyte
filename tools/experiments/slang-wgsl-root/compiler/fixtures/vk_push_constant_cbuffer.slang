import shared_math;
[[vk::push_constant]] cbuffer RootData { uint value; uint bias; };
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    output[0] = evaluateRoot(value, bias);
}
