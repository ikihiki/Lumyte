using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal static class VulkanMeshValidation
{
    internal static PrimitiveTopology Program(VulkanDevice owner, MeshPipelineDesc desc, ShaderTargetData mesh, ShaderTargetData? task, ShaderTargetData? fragment)
    {
        MeshShaderValidation.Validate(mesh, owner.Caps);
        if (task != null)
        {
            MeshShaderValidation.Validate(task, owner.Caps);
        }

        MeshShaderValidation.Program(mesh, task, fragment);
        if (!Enum.IsDefined(desc.Optimization) || (desc.AlphaToCoverageEnable && fragment == null))
        {
            throw new ArgumentException("Invalid Mesh compilation options.");
        }

        JsonElement metadata = MeshShaderMetadata.Read(mesh);
        PrimitiveTopology topology = metadata.GetProperty("outputTopology").GetString() switch
        {
            "line" => PrimitiveTopology.LineList,
            "triangle" => PrimitiveTopology.TriangleList,
            _ => throw new NotSupportedException("Point Mesh output is not supported."),
        };
        Silk.NET.Vulkan.PhysicalDeviceMeshShaderPropertiesEXT p = owner.MeshProperties;

        // SPIR-V output storage is charged at the implementation's vertex/primitive
        // allocation granularity, not simply the declared unrounded byte count.
        ulong vertexCount = metadata.GetProperty("maxOutputVertices").GetUInt32();
        ulong primitiveCount = metadata.GetProperty("maxOutputPrimitives").GetUInt32();
        ulong vertexComponents = 0;
        ulong primitiveComponents = 0;
        foreach (JsonElement output in metadata.GetProperty("outputs").EnumerateArray())
        {
            ulong components = Round(Components(output.GetProperty("type").GetString()!), 4);
            bool perPrimitive = output.TryGetProperty("perPrimitive", out JsonElement per) && per.GetBoolean();
            if (perPrimitive)
            {
                primitiveComponents += components;
            }
            else
            {
                vertexComponents += components;
            }
        }

        if (vertexComponents + primitiveComponents > p.MaxMeshOutputComponents)
        {
            throw new NotSupportedException("Mesh output components exceed the enabled Vulkan limit.");
        }

        ulong vertices = Round(vertexCount, p.MeshOutputPerVertexGranularity);
        ulong primitives = Round(primitiveCount, p.MeshOutputPerPrimitiveGranularity);

        // Primitive indices are excluded; each occupied location consumes four scalars.
        ulong outputBytes = checked(4 * ((vertices * vertexComponents) + (primitives * primitiveComponents)));
        ulong payload = metadata.GetProperty("payloadBytes").GetUInt64();
        if (outputBytes > p.MaxMeshOutputMemorySize || outputBytes > p.MaxMeshPayloadAndOutputMemorySize || payload > p.MaxMeshPayloadAndOutputMemorySize - outputBytes)
        {
            throw new NotSupportedException("Rounded Vulkan Mesh output storage exceeds enabled memory limits.");
        }

        return topology;
    }

    private static ulong Round(ulong value, uint granularity) => granularity == 0
        ? throw new NotSupportedException("Vulkan reported an invalid Mesh output granularity.")
        : checked((value + granularity - 1) / granularity * granularity);

    private static ulong Components(string type)
    {
        char last = type[^1];
        ulong count = last is >= '1' and <= '4' ? (ulong)(last - '0') : 1;
        return count * (type.Contains("64", StringComparison.Ordinal) || type.Contains("double", StringComparison.Ordinal) ? 2UL : 1UL);
    }
}
