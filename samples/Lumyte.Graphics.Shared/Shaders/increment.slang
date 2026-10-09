RWStructuredBuffer<uint> values;

[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 index : SV_DispatchThreadID)
{
    values[index.x] = values[index.x] + 1;
}
