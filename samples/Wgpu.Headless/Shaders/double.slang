RWStructuredBuffer<uint> values;

[shader("compute")]
[numthreads(64, 1, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    uint count, stride;
    values.GetDimensions(count, stride);
    if (id.x < count) values[id.x] *= 2;
}
