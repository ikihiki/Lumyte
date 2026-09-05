StructuredBuffer<float4> pathData : register(t0, space2);
RWStructuredBuffer<float4> preparedPathData : register(u0, space4);

static const uint HeaderVectorCount = 16;
static const uint BrushSolid = 0;
static const uint BrushLinearGradient = 1;
static const uint BrushRadialGradient = 2;
static const uint BrushSweepGradient = 3;
static const uint ExtendPad = 0;
static const uint ExtendRepeat = 1;
static const uint ExtendReflect = 2;
static const float Pi = 3.14159265358979323846;

float pointLineDistance(float2 point, float2 start, float2 end)
{
    float2 segment = end - start;
    float denominator = dot(segment, segment);
    if (denominator == 0.0) return length(point - start);
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
    float ratio = tolerance > 0.0 ? error / tolerance : error;
    return min(max(uint(ceil(sqrt(max(ratio, 1.0)))), 1u), 64u);
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
            if (any(next != previous) && outputCount < outputCapacity)
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

    float4 parameters = pathData[3];
    float4 tileGrid = pathData[7];
    float4 outputMetadata = pathData[8];
    float4 inputMetadata = pathData[9];
    float4 auxiliaryOffsets = pathData[10];
    uint mainCount = expandSegments(
        uint(inputMetadata.x),
        uint(parameters.x),
        uint(outputMetadata.y),
        uint(outputMetadata.z),
        inputMetadata.y);

    for (uint index = 0; index < HeaderVectorCount; index++)
    {
        preparedPathData[index] = pathData[index];
    }
    preparedPathData[8] = float4(outputMetadata.xy, float(mainCount), outputMetadata.w);

    uint clipCount = uint(inputMetadata.z);
    uint clipInputStart = uint(inputMetadata.w);
    uint clipOutputStart = uint(auxiliaryOffsets.x);
    for (uint clipIndex = 0; clipIndex < clipCount; clipIndex++)
    {
        float4 descriptor = pathData[clipInputStart + clipIndex * 2];
        float4 placement = pathData[clipInputStart + clipIndex * 2 + 1];
        uint expandedCount = expandSegments(
            uint(descriptor.x),
            uint(descriptor.y),
            uint(placement.x),
            uint(descriptor.z),
            placement.z);
        preparedPathData[clipOutputStart + clipIndex] = float4(
            placement.x,
            float(expandedCount),
            descriptor.w,
            placement.y);
    }

    uint gradientCount = uint(pathData[6].w);
    uint gradientInputStart = uint(auxiliaryOffsets.z);
    uint gradientOutputStart = uint(auxiliaryOffsets.y);
    for (uint index = 0; index < gradientCount * 2; index++)
    {
        preparedPathData[gradientOutputStart + index] = pathData[gradientInputStart + index];
    }

    uint columns = uint(tileGrid.x);
    uint rows = uint(tileGrid.y);
    uint tileCount = uint(outputMetadata.w);
    uint tableStart = uint(tileGrid.w);
    uint indexStart = uint(outputMetadata.x);
    uint edgeStart = uint(outputMetadata.y);
    float4 bounds = pathData[0];
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
        preparedPathData[tableStart + tileIndex] = float4(0.0, float(count), 0.0, 0.0);
    }

    uint indexCount = 0;
    for (uint tileIndex = 0; tileIndex < tileCount; tileIndex++)
    {
        uint tableIndex = tableStart + tileIndex;
        float4 tile = preparedPathData[tableIndex];
        preparedPathData[tableIndex] = float4(float(indexStart + indexCount), tile.y, 0.0, 0.0);
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
    float2 worldPosition : TEXCOORD1;
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
    float4 bounds = pathData[0];
    float4 transform0 = pathData[1];
    float4 transform1 = pathData[2];
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
    output.worldPosition = world;
    return output;
}

float segmentDistance(float2 point, float4 edge)
{
    float2 segment = edge.zw - edge.xy;
    float denominator = dot(segment, segment);
    if (denominator == 0.0) return length(point - edge.xy);
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
    if (crosses)
    {
        float crossX = edge.x + (point.y - edge.y)
            * (edge.z - edge.x) / (edge.w - edge.y);
        if (crossX > point.x)
        {
            odd = !odd;
            winding += edge.w > edge.y ? 1 : -1;
        }
    }
}

float pathCoverage(float2 point)
{
    float4 parameters = pathData[3];
    float4 tileGrid = pathData[7];
    float4 outputMetadata = pathData[8];
    float4 bounds = pathData[0];
    uint columns = uint(tileGrid.x);
    uint rows = uint(tileGrid.y);
    uint tileX = min(uint(max(floor((point.x - bounds.x) / tileGrid.z), 0.0)), columns - 1);
    uint tileY = min(uint(max(floor((point.y - bounds.y) / tileGrid.z), 0.0)), rows - 1);
    float4 tile = pathData[uint(tileGrid.w) + tileY * columns + tileX];
    float minimumDistance = 1000000.0;
    int winding = 0;
    bool odd = false;
    for (uint item = 0; item < uint(tile.y); item++)
    {
        uint edgeIndex = uint(pathData[uint(tile.x) + item].x);
        includeEdge(point, pathData[uint(outputMetadata.y) + edgeIndex], minimumDistance, winding, odd);
    }
    bool inside = uint(parameters.y) == 1 ? odd : winding != 0;
    float distance = parameters.z > 0.0
        ? minimumDistance - parameters.z * 0.5
        : (inside ? -minimumDistance : minimumDistance);
    return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
}

float oneClipCoverage(float2 point, float4 descriptor)
{
    uint count = uint(descriptor.y);
    if (count == 0) return 0.0;
    float minimumDistance = 1000000.0;
    int winding = 0;
    bool odd = false;
    for (uint index = 0; index < count; index++)
    {
        includeEdge(point, pathData[uint(descriptor.x) + index], minimumDistance, winding, odd);
    }
    bool inside = uint(descriptor.z) == 1 ? odd : winding != 0;
    float distance = inside ? -minimumDistance : minimumDistance;
    return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
}

float clipCoverage(float2 localPoint, float2 worldPoint)
{
    uint count = uint(pathData[9].z);
    uint descriptorStart = uint(pathData[10].x);
    float coverage = 1.0;
    for (uint index = 0; index < count; index++)
    {
        float4 descriptor = pathData[descriptorStart + index];
        float2 point = descriptor.w != 0.0 ? worldPoint : localPoint;
        coverage *= oneClipCoverage(point, descriptor);
    }
    return coverage;
}

float2 applyExtend(float value, uint mode)
{
    uint stopCount = uint(pathData[6].w);
    if (stopCount <= 1) return float2(value, 1.0);
    uint stopStart = uint(pathData[10].y);
    float first = pathData[stopStart].x;
    float last = pathData[stopStart + (stopCount - 1) * 2].x;
    if (mode == ExtendPad) return float2(value, 1.0);

    float width = last - first;
    if (width <= 0.0) return float2(0.0, 0.0);
    if (value >= first && value <= last) return float2(value, 1.0);

    float normalized = (value - first) / width;
    if (mode == ExtendRepeat)
    {
        float repeated = normalized - floor(normalized);
        // The defined and repeated intervals are closed on their upper edge.
        if (value > last && repeated == 0.0) repeated = 1.0;
        return float2(first + repeated * width, 1.0);
    }
    if (mode == ExtendReflect)
    {
        float period = normalized - floor(normalized * 0.5) * 2.0;
        float reflected = period <= 1.0 ? period : 2.0 - period;
        return float2(first + reflected * width, 1.0);
    }
    return float2(value, 1.0);
}

float4 readGradientStop(uint start, uint index)
{
    float4 offsetAndRgb = pathData[start + index * 2];
    float alpha = pathData[start + index * 2 + 1].x;
    return float4(offsetAndRgb.yzw, alpha);
}

float4 evaluateColorLine(float value)
{
    uint stopCount = uint(pathData[6].w);
    if (stopCount == 0) return pathData[11];

    uint stopStart = uint(pathData[10].y);
    float firstOffset = pathData[stopStart].x;
    float4 firstColor = readGradientStop(stopStart, 0);
    if (stopCount == 1 || value < firstOffset) return firstColor;

    float previousOffset = firstOffset;
    float4 previousColor = firstColor;
    for (uint index = 1; index < stopCount; index++)
    {
        float currentOffset = pathData[stopStart + index * 2].x;
        float4 currentColor = readGradientStop(stopStart, index);
        if (value < currentOffset)
        {
            float width = currentOffset - previousOffset;
            if (width <= 0.0) return currentColor;
            return lerp(previousColor, currentColor, saturate((value - previousOffset) / width));
        }
        previousOffset = currentOffset;
        previousColor = currentColor;
    }
    return previousColor;
}

float2 linearGradientParameter(float2 point)
{
    float4 anchors = pathData[4];
    float2 point0 = anchors.xy;
    float2 point1 = anchors.zw;
    float2 point2 = pathData[5].xy;
    float2 projectionDirection = float2(-(point2.y - point0.y), point2.x - point0.x);
    float denominator = dot(point1 - point0, projectionDirection);
    if (denominator == 0.0) return float2(0.0, 0.0);
    return float2(dot(point - point0, projectionDirection) / denominator, 1.0);
}

float2 radialGradientParameter(float2 point)
{
    float4 circles = pathData[4];
    float4 radii = pathData[5];
    float2 center0 = circles.xy;
    float2 centerDelta = circles.zw - center0;
    float radius0 = radii.z;
    float radiusDelta = radii.w - radius0;
    float2 relative = point - center0;
    if (all(centerDelta == 0.0) && radiusDelta == 0.0)
    {
        return float2(0.0, 0.0);
    }
    if (radius0 == 0.0 && radii.w == 0.0)
    {
        return float2(0.0, 0.0);
    }
    float a = dot(centerDelta, centerDelta) - radiusDelta * radiusDelta;
    float b = -2.0 * (dot(relative, centerDelta) + radius0 * radiusDelta);
    float c = dot(relative, relative) - radius0 * radius0;

    if (a == 0.0)
    {
        if (b == 0.0)
        {
            return c == 0.0 ? float2(0.0, 1.0) : float2(0.0, 0.0);
        }
        float value = -c / b;
        return float2(value, radius0 + value * radiusDelta >= 0.0 ? 1.0 : 0.0);
    }

    float discriminant = b * b - 4.0 * a * c;
    if (discriminant < 0.0) return float2(0.0, 0.0);
    float root = sqrt(max(discriminant, 0.0));
    float first = (-b - root) / (2.0 * a);
    float second = (-b + root) / (2.0 * a);
    bool firstValid = radius0 + first * radiusDelta >= 0.0;
    bool secondValid = radius0 + second * radiusDelta >= 0.0;
    if (!firstValid && !secondValid) return float2(0.0, 0.0);
    if (!firstValid) return float2(second, 1.0);
    if (!secondValid) return float2(first, 1.0);
    return float2(max(first, second), 1.0);
}

float2 sweepGradientParameter(float2 point)
{
    float2 center = pathData[4].xy;
    float2 angles = pathData[6].xy;
    float span = angles.y - angles.x;
    uint extendMode = uint(pathData[6].z);
    // COLRv1 angles are counter-clockwise in its y-up design space. The text
    // reader mirrors that space into our y-down coordinates, so one sampled
    // revolution is represented by (-2pi, 0]. Do not lift the 0-degree ray to
    // another revolution: ranges such as 330..400 intentionally stay clipped
    // at the 360-degree drawing boundary.
    float angle = atan2(point.y - center.y, point.x - center.x);
    if (angle > 0.0) angle -= 2.0 * Pi;
    if (span == 0.0)
    {
        if (extendMode != ExtendPad) return float2(0.0, 0.0);
        return float2(angle > angles.x ? -1.0 : 1.0, 1.0);
    }
    return float2((angle - angles.x) / span, 1.0);
}

float4 evaluateBrush(float2 point)
{
    uint kind = uint(pathData[3].w);
    if (kind == BrushSolid) return pathData[11];

    float2 parameter;
    if (kind == BrushLinearGradient)
    {
        parameter = linearGradientParameter(point);
    }
    else if (kind == BrushRadialGradient)
    {
        parameter = radialGradientParameter(point);
    }
    else if (kind == BrushSweepGradient)
    {
        parameter = sweepGradientParameter(point);
    }
    else
    {
        return float4(0.0);
    }
    if (parameter.y == 0.0) return float4(0.0);
    float2 extended = applyExtend(parameter.x, uint(pathData[6].z));
    return extended.y == 0.0 ? float4(0.0) : evaluateColorLine(extended.x);
}

[shader("fragment")]
float4 pathPixel(PathVertexOutput input) : SV_Target0
{
    return evaluateBrush(input.localPosition)
        * pathCoverage(input.localPosition)
        * clipCoverage(input.localPosition, input.worldPosition);
}
