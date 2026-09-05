StructuredBuffer<float4> geometryBuffer : register(t0, space2);
Texture2D<float4> imageTexture : register(t0, space0);
SamplerState imageSampler : register(s0, space1);

static const uint CommandRectangle = 1;
static const uint CommandRoundedRectangle = 2;
static const uint CommandEllipse = 3;
static const uint CommandLine = 4;

struct PrimitiveVertexOutput
{
    float4 position : SV_Position;
    float2 localPosition : TEXCOORD0;
    float4 bounds : TEXCOORD1;
    float4 color : TEXCOORD2;
    float4 parameters0 : TEXCOORD3;
    float4 parameters1 : TEXCOORD4;
    float4 textureRegion : TEXCOORD5;
    nointerpolation uint kind : TEXCOORD6;
};

float2 quadCoordinate(uint vertexIndex)
{
    if (vertexIndex == 0 || vertexIndex == 3)
    {
        return float2(0.0, 0.0);
    }
    if (vertexIndex == 1)
    {
        return float2(1.0, 0.0);
    }
    if (vertexIndex == 5)
    {
        return float2(0.0, 1.0);
    }
    return float2(1.0, 1.0);
}

PrimitiveVertexOutput readPrimitive(uint vertexIndex, uint instanceIndex)
{
    uint offset = instanceIndex * 8;
    float4 header = geometryBuffer[offset];
    float4 bounds = geometryBuffer[offset + 1];
    float4 color = geometryBuffer[offset + 2];
    float4 parameters0 = geometryBuffer[offset + 3];
    float4 parameters1 = geometryBuffer[offset + 4];
    float4 textureRegion = geometryBuffer[offset + 5];
    float4 transform0 = geometryBuffer[offset + 6];
    float4 transform1 = geometryBuffer[offset + 7];

    float2 coordinate = quadCoordinate(vertexIndex);
    float2 localPosition = bounds.xy + coordinate * bounds.zw;
    float2 worldPosition = float2(
        localPosition.x * transform0.x + localPosition.y * transform0.z + transform1.x,
        localPosition.x * transform0.y + localPosition.y * transform0.w + transform1.y);
    float2 clipPosition = float2(
        worldPosition.x / transform1.z * 2.0 - 1.0,
        1.0 - worldPosition.y / transform1.w * 2.0);

    PrimitiveVertexOutput output;
    output.position = float4(clipPosition, 0.0, 1.0);
    output.localPosition = localPosition;
    output.bounds = bounds;
    output.color = color;
    output.parameters0 = parameters0;
    output.parameters1 = parameters1;
    output.textureRegion = textureRegion;
    output.kind = uint(header.x);
    return output;
}

[shader("vertex")]
PrimitiveVertexOutput primitiveVertex(
    uint vertexIndex : SV_VertexID,
    uint instanceIndex : SV_InstanceID)
{
    return readPrimitive(vertexIndex, instanceIndex);
}

float rectangleDistance(float2 position, float4 bounds)
{
    float2 center = bounds.xy + bounds.zw * 0.5;
    float2 halfSize = bounds.zw * 0.5;
    float2 value = abs(position - center) - halfSize;
    return length(max(value, 0.0)) + min(max(value.x, value.y), 0.0);
}

float roundedRectangleDistance(float2 position, float4 bounds, float4 radii)
{
    float2 center = bounds.xy + bounds.zw * 0.5;
    float2 centered = position - center;
    float radius = centered.y < 0.0
        ? (centered.x < 0.0 ? radii.x : radii.y)
        : (centered.x < 0.0 ? radii.w : radii.z);
    float2 value = abs(centered) - bounds.zw * 0.5 + radius;
    return min(max(value.x, value.y), 0.0) + length(max(value, 0.0)) - radius;
}

float ellipseDistance(float2 position, float4 bounds)
{
    float2 halfSize = max(bounds.zw * 0.5, 0.0001);
    float2 centered = (position - bounds.xy - halfSize) / halfSize;
    return (length(centered) - 1.0) * min(halfSize.x, halfSize.y);
}

float lineDistance(float2 position, float4 endpoints, float width)
{
    float2 start = endpoints.xy;
    float2 segment = endpoints.zw - start;
    float denominator = max(dot(segment, segment), 0.0001);
    float amount = saturate(dot(position - start, segment) / denominator);
    return length(position - start - segment * amount) - width * 0.5;
}

float antialiasedCoverage(float distance)
{
    return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
}

[shader("fragment")]
float4 primitivePixel(PrimitiveVertexOutput input) : SV_Target0
{
    float distance = input.kind == CommandRoundedRectangle
        ? roundedRectangleDistance(input.localPosition, input.bounds, input.parameters0)
        : input.kind == CommandEllipse
            ? ellipseDistance(input.localPosition, input.bounds)
            : input.kind == CommandLine
                ? lineDistance(input.localPosition, input.parameters0, input.parameters1.x)
                : rectangleDistance(input.localPosition, input.bounds);
    return input.color * antialiasedCoverage(distance);
}

[shader("vertex")]
PrimitiveVertexOutput imageVertex(
    uint vertexIndex : SV_VertexID,
    uint instanceIndex : SV_InstanceID)
{
    return readPrimitive(vertexIndex, instanceIndex);
}

[shader("fragment")]
float4 imagePixel(PrimitiveVertexOutput input) : SV_Target0
{
    float2 relative = (input.localPosition - input.bounds.xy) / input.bounds.zw;
    float2 uv = input.textureRegion.xy + relative * input.textureRegion.zw;
    float4 sampled = imageTexture.Sample(imageSampler, uv);
    sampled.rgb *= sampled.a;
    float coverage = antialiasedCoverage(rectangleDistance(input.localPosition, input.bounds));
    return float4(
        sampled.rgb * input.color.rgb,
        sampled.a * input.color.a) * coverage;
}

[shader("vertex")]
PrimitiveVertexOutput distanceFieldVertex(
    uint vertexIndex : SV_VertexID,
    uint instanceIndex : SV_InstanceID)
{
    return readPrimitive(vertexIndex, instanceIndex);
}

[shader("fragment")]
float4 distanceFieldPixel(PrimitiveVertexOutput input) : SV_Target0
{
    float2 relative = (input.localPosition - input.bounds.xy) / input.bounds.zw;
    float2 uv = input.textureRegion.xy + relative * input.textureRegion.zw;
    float4 distanceSample = imageTexture.Sample(imageSampler, uv);
    float value = input.parameters0.y == 2.0
        ? max(min(distanceSample.r, distanceSample.g), min(max(distanceSample.r, distanceSample.g), distanceSample.b))
        : distanceSample.r;
    float coverage = input.parameters0.y == 0.0
        ? value
        : saturate((value - 0.5) / max(fwidth(value), 0.0001) + 0.5);
    return input.color * coverage;
}

struct PolygonVertexOutput
{
    float4 position : SV_Position;
    float4 color : TEXCOORD0;
};

[shader("vertex")]
PolygonVertexOutput polygonVertex(uint vertexIndex : SV_VertexID)
{
    float4 color = geometryBuffer[0];
    float4 transform0 = geometryBuffer[1];
    float4 transform1 = geometryBuffer[2];
    float2 localPosition = geometryBuffer[4 + vertexIndex].xy;
    float2 worldPosition = float2(
        localPosition.x * transform0.x + localPosition.y * transform0.z + transform1.x,
        localPosition.x * transform0.y + localPosition.y * transform0.w + transform1.y);
    float2 clipPosition = float2(
        worldPosition.x / transform1.z * 2.0 - 1.0,
        1.0 - worldPosition.y / transform1.w * 2.0);

    PolygonVertexOutput output;
    output.position = float4(clipPosition, 0.0, 1.0);
    output.color = color;
    return output;
}

[shader("fragment")]
float4 polygonPixel(PolygonVertexOutput input) : SV_Target0
{
    return input.color;
}
