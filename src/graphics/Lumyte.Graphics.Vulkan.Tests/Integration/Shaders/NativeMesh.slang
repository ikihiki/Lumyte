// Direct root data and device pointers. No compiler Y inversion or buffer root emulation.
#include "NativeRaster.slang"

struct MeshVertex
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

struct TaskPayload
{
    float4 color;
    float offset;
};

[shader("mesh")]
[numthreads(1, 1, 1)]
[outputtopology("triangle")]
void meshMain(out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
{
    SetMeshOutputCounts(3, 1);
    float2* positions = (float2*)root.vertices;
    for (uint i = 0; i < 3; ++i)
    {
        vertices[i].position = float4(positions[i], root.depth, 1);
        vertices[i].color = root.color;
    }
    triangles[0] = uint3(0, 1, 2);
}

[shader("amplification")]
[numthreads(1, 1, 1)]
void taskMain()
{
    TaskPayload data;
    data.color = root.color;
    data.offset = root.instanceOffset;
    DispatchMesh(1, 1, 1, data);
}

[shader("mesh")]
[numthreads(1, 1, 1)]
[outputtopology("triangle")]
void payloadMeshMain(in payload TaskPayload data, out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
{
    SetMeshOutputCounts(3, 1);
    float2* positions = (float2*)root.vertices;
    for (uint i = 0; i < 3; ++i)
    {
        vertices[i].position = float4(positions[i] + float2(data.offset, 0), root.depth, 1);
        vertices[i].color = data.color;
    }
    triangles[0] = uint3(0, 1, 2);
}

[shader("mesh")]
[numthreads(1, 1, 1)]
[outputtopology("triangle")]
void groupMeshMain(uint3 group : SV_GroupID, out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
{
    // Only the (1, 2, 3) group emits a triangle, proving all command group axes reach the GPU.
    bool visible = all(group == uint3(1, 2, 3));
    SetMeshOutputCounts(visible ? 3 : 0, visible ? 1 : 0);
    if (visible)
    {
        float2* positions = (float2*)root.vertices;
        for (uint i = 0; i < 3; ++i)
        {
            vertices[i].position = float4(positions[i], root.depth, 1);
            vertices[i].color = root.color;
        }
        triangles[0] = uint3(0, 1, 2);
    }
}

[shader("mesh")]
[numthreads(1, 1, 1)]
[outputtopology("line")]
void lineMeshMain(out vertices MeshVertex vertices[2], out indices uint2 lines[1])
{
    SetMeshOutputCounts(2, 1);
    vertices[0].position = float4(-1, 0.125, root.depth, 1);
    vertices[1].position = float4(1, 0.125, root.depth, 1);
    vertices[0].color = root.color;
    vertices[1].color = root.color;
    lines[0] = uint2(0, 1);
}

[shader("fragment")]
float4 meshPixelMain(MeshVertex vertex) : SV_Target0
{
    return vertex.color + float4(float(root.tail.w) / 255, 0, 0, 0);
}

[shader("compute")]
[numthreads(1, 1, 1)]
void meshArgumentsMain()
{
    uint* arguments = (uint*)root.arguments;
    arguments[0] = 2;
    arguments[1] = 3;
    arguments[2] = 4;
}
