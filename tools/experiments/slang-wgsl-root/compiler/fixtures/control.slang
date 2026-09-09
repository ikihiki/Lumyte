import shared_math;
[[vk::binding(0, 0)]] RWStructuredBuffer<uint> output;
[shader("compute")]
[numthreads(1, 1, 1)]
void main()
{
    output[0] = evaluateRoot(11u, 7u);
}
