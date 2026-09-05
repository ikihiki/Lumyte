StructuredBuffer<float4> pathData : register(t0, space2);
RWStructuredBuffer<float4> preparedPathData : register(u0, space4);

float pointLineDistance(float2 point, float2 start, float2 end)
{
    float2 segment = end - start;
    float denominator = max(dot(segment, segment), 0.000001);
    float amount = saturate(dot(point - start, segment) / denominator);
    return length(point - start - segment * amount);
}

uint subdivisionCount(uint kind, float2 start, float2 end, float4 controls, float tolerance)
{
    if (kind == 1 || kind == 4) return 1;
    float error = kind == 2
        ? pointLineDistance(controls.xy, start, end)
        : max(
            pointLineDistance(controls.xy, start, end),
            pointLineDistance(controls.zw, start, end));
    return min(max(uint(ceil(sqrt(max(error / max(tolerance, 0.0001), 1.0)))), 1u), 64u);
}

float2 evaluateSegment(
    uint kind,
    float2 start,
    float2 end,
    float4 controls,
    float amount)
{
    if (kind == 2)
    {
        float inverse = 1.0 - amount;
        return inverse * inverse * start
            + 2.0 * inverse * amount * controls.xy
            + amount * amount * end;
    }
    if (kind == 3)
    {
        float inverse = 1.0 - amount;
        return inverse * inverse * inverse * start
            + 3.0 * inverse * inverse * amount * controls.xy
            + 3.0 * inverse * amount * amount * controls.zw
            + amount * amount * amount * end;
    }
    return lerp(start, end, amount);
}

uint expandSegments(
    uint inputStart,
    uint segmentCount,
    uint outputStart,
    uint outputCapacity,
    float tolerance)
{
    float2 current = float2(0.0);
    float2 figureStart = float2(0.0);
    uint outputCount = 0;
    for (uint segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
    {
        float4 segment = pathData[inputStart + segmentIndex * 2];
        float4 controls = pathData[inputStart + segmentIndex * 2 + 1];
        uint kind = uint(segment.x);
        float2 end = segment.yz;
        if (kind == 0)
        {
            current = end;
            figureStart = end;
            continue;
        }
        if (kind == 4) end = figureStart;
        uint subdivisions = subdivisionCount(kind, current, end, controls, tolerance);
        float2 previous = current;
        for (uint step = 1; step <= subdivisions; step++)
        {
            float2 next = evaluateSegment(
                kind,
                current,
                end,
                controls,
                float(step) / float(subdivisions));
            if (dot(next - previous, next - previous) > 0.00000001 && outputCount < outputCapacity)
            {
                preparedPathData[outputStart + outputCount] = float4(previous, next);
                outputCount++;
            }
            previous = next;
        }
        current = end;
    }
    return outputCount;
}

bool edgeTouchesTile(float4 edge, float4 tile, float expansion)
{
    float edgeLeft = min(edge.x, edge.z);
    float edgeRight = max(edge.x, edge.z);
    float edgeTop = min(edge.y, edge.w);
    float edgeBottom = max(edge.y, edge.w);
    bool crossesRay = edgeBottom >= tile.y && edgeTop <= tile.w && edgeRight >= tile.x;
    bool nearTile = edgeRight >= tile.x - expansion && edgeLeft <= tile.z + expansion
        && edgeBottom >= tile.y - expansion && edgeTop <= tile.w + expansion;
    return crossesRay || nearTile;
}

[shader("compute")]
[numthreads(1, 1, 1)]
void preparePath(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0))) return;

    float4 parameters = pathData[5];
    float4 tileGrid = pathData[7];
    float4 clipMetadata = pathData[8];
    float4 sourceOffsets = pathData[9];
    float4 capacities = pathData[10];
    float4 stroke = pathData[11];
    uint edgeStart = 10;
    uint mainCount = expandSegments(
        uint(sourceOffsets.w),
        uint(parameters.x),
        edgeStart,
        uint(capacities.x),
        capacities.w);
    uint clipCount = expandSegments(
        uint(capacities.z),
        uint(clipMetadata.z),
        uint(clipMetadata.y),
        uint(capacities.y),
        capacities.w);

    preparedPathData[0] = pathData[0];
    preparedPathData[1] = pathData[1];
    preparedPathData[2] = pathData[2];
    preparedPathData[3] = pathData[3];
    preparedPathData[4] = pathData[4];
    preparedPathData[5] = float4(mainCount, parameters.yzw);
    preparedPathData[6] = pathData[6];
    preparedPathData[7] = tileGrid;
    preparedPathData[8] = float4(clipMetadata.xy, clipCount, clipMetadata.w);
    preparedPathData[9] = float4(sourceOffsets.xy, edgeStart, 0.0);

    uint columns = uint(tileGrid.x);
    uint rows = uint(tileGrid.y);
    uint tileCount = uint(stroke.w);
    uint tableStart = uint(tileGrid.w);
    uint indexStart = uint(clipMetadata.x);
    float4 bounds = pathData[2];
    float expansion = max(parameters.z * 0.5 + 1.0, 1.0);
    for (uint tileIndex = 0; tileIndex < tileCount; tileIndex++)
    {
        uint tileX = tileIndex % columns;
        uint tileY = tileIndex / columns;
        float left = bounds.x + float(tileX) * tileGrid.z;
        float top = bounds.y + float(tileY) * tileGrid.z;
        float right = tileX + 1 == columns ? bounds.x + bounds.z : left + tileGrid.z;
        float bottom = tileY + 1 == rows ? bounds.y + bounds.w : top + tileGrid.z;
        uint count = 0;
        for (uint edgeIndex = 0; edgeIndex < mainCount; edgeIndex++)
        {
            if (edgeTouchesTile(
                preparedPathData[edgeStart + edgeIndex],
                float4(left, top, right, bottom),
                expansion))
            {
                count++;
            }
        }
        preparedPathData[tableStart + tileIndex] = float4(0.0, count, 0.0, 0.0);
    }

    uint indexCount = 0;
    for (uint tileIndex = 0; tileIndex < tileCount; tileIndex++)
    {
        uint tableIndex = tableStart + tileIndex;
        float4 tile = preparedPathData[tableIndex];
        preparedPathData[tableIndex] = float4(indexStart + indexCount, tile.y, 0.0, 0.0);
        indexCount += uint(tile.y);
    }

    for (uint tileIndex = 0; tileIndex < tileCount; tileIndex++)
    {
        uint tileX = tileIndex % columns;
        uint tileY = tileIndex / columns;
        float left = bounds.x + float(tileX) * tileGrid.z;
        float top = bounds.y + float(tileY) * tileGrid.z;
        float right = tileX + 1 == columns ? bounds.x + bounds.z : left + tileGrid.z;
        float bottom = tileY + 1 == rows ? bounds.y + bounds.w : top + tileGrid.z;
        uint writeIndex = uint(preparedPathData[tableStart + tileIndex].x);
        for (uint edgeIndex = 0; edgeIndex < mainCount; edgeIndex++)
        {
            if (edgeTouchesTile(
                preparedPathData[edgeStart + edgeIndex],
                float4(left, top, right, bottom),
                expansion))
            {
                preparedPathData[writeIndex] = float4(float(edgeIndex), 0.0, 0.0, 0.0);
                writeIndex++;
            }
        }
    }
}

struct PathVertexOutput
{
    float4 position : SV_Position;
    float2 localPosition : TEXCOORD0;
};

float2 pathQuadCoordinate(uint vertexIndex)
{
    if (vertexIndex == 0 || vertexIndex == 3) return float2(0.0, 0.0);
    if (vertexIndex == 1) return float2(1.0, 0.0);
    if (vertexIndex == 5) return float2(0.0, 1.0);
    return float2(1.0, 1.0);
}

[shader("vertex")]
PathVertexOutput pathVertex(uint vertexIndex : SV_VertexID)
{
    float4 bounds = pathData[2];
    float4 transform0 = pathData[3];
    float4 transform1 = pathData[4];
    float2 coordinate = pathQuadCoordinate(vertexIndex);
    float2 local = bounds.xy + coordinate * bounds.zw;
    float2 world = float2(
        local.x * transform0.x + local.y * transform0.z + transform1.x,
        local.x * transform0.y + local.y * transform0.w + transform1.y);

    PathVertexOutput output;
    output.position = float4(
        world.x / transform1.z * 2.0 - 1.0,
        1.0 - world.y / transform1.w * 2.0,
        0.0,
        1.0);
    output.localPosition = local;
    return output;
}

float segmentDistance(float2 point, float4 edge)
{
    float2 segment = edge.zw - edge.xy;
    float denominator = max(dot(segment, segment), 0.000001);
    float amount = saturate(dot(point - edge.xy, segment) / denominator);
    return length(point - edge.xy - segment * amount);
}

void includeEdge(
    float2 point,
    float4 edge,
    inout float minimumDistance,
    inout int winding,
    inout bool odd)
{
    minimumDistance = min(minimumDistance, segmentDistance(point, edge));
    bool crosses = (edge.y > point.y) != (edge.w > point.y);
    float direction = edge.w >= edge.y ? 1.0 : -1.0;
    float crossX = edge.x + (point.y - edge.y)
        * (edge.z - edge.x) / max(abs(edge.w - edge.y), 0.000001) * direction;
    if (crosses && crossX > point.x)
    {
        odd = !odd;
        winding += edge.w > edge.y ? 1 : -1;
    }
}

float pathCoverage(float2 point)
{
    float4 parameters = pathData[5];
    float4 tileGrid = pathData[7];
    float4 indices = pathData[8];
    float4 origin = pathData[9];
    uint columns = uint(tileGrid.x);
    uint rows = uint(tileGrid.y);
    uint tileX = min(uint(max(floor((point.x - origin.x) / tileGrid.z), 0.0)), columns - 1);
    uint tileY = min(uint(max(floor((point.y - origin.y) / tileGrid.z), 0.0)), rows - 1);
    float4 tile = pathData[uint(tileGrid.w) + tileY * columns + tileX];
    float minimumDistance = 1000000.0;
    int winding = 0;
    bool odd = false;
    for (uint item = 0; item < uint(tile.y); item++)
    {
        uint edgeIndex = uint(pathData[uint(tile.x) + item].x);
        includeEdge(point, pathData[uint(origin.z) + edgeIndex], minimumDistance, winding, odd);
    }
    bool inside = uint(parameters.y) == 1 ? odd : winding != 0;
    float distance = parameters.z > 0.0
        ? minimumDistance - parameters.z * 0.5
        : (inside ? -minimumDistance : minimumDistance);
    return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
}

float clipCoverage(float2 point)
{
    float4 metadata = pathData[8];
    uint count = uint(metadata.z);
    if (count == 0) return 1.0;
    float minimumDistance = 1000000.0;
    int winding = 0;
    bool odd = false;
    for (uint index = 0; index < count; index++)
    {
        includeEdge(point, pathData[uint(metadata.y) + index], minimumDistance, winding, odd);
    }
    bool inside = uint(metadata.w) == 1 ? odd : winding != 0;
    float distance = inside ? -minimumDistance : minimumDistance;
    return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
}

float4 evaluateBrush(float2 point)
{
    float4 first = pathData[0];
    float4 second = pathData[1];
    float4 parameters = pathData[5];
    float4 geometry = pathData[6];
    if (uint(parameters.w) == 1)
    {
        float2 direction = geometry.zw - geometry.xy;
        float amount = saturate(dot(point - geometry.xy, direction) / max(dot(direction, direction), 0.000001));
        return lerp(first, second, amount);
    }
    if (uint(parameters.w) == 2)
    {
        float amount = saturate(length(point - geometry.xy) / max(geometry.z, 0.000001));
        return lerp(first, second, amount);
    }
    return first;
}

[shader("fragment")]
float4 pathPixel(PathVertexOutput input) : SV_Target0
{
    return evaluateBrush(input.localPosition)
        * pathCoverage(input.localPosition)
        * clipCoverage(input.localPosition);
}
