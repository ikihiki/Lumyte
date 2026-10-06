RWStructuredBuffer<uint> resultBuffer;

[shader("compute")]
[numthreads(1, 1, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    resultBuffer[dispatchThreadID.x] = dispatchThreadID.x + 1;
}
