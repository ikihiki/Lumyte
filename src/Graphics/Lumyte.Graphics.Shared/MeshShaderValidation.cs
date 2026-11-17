using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Validates portable mesh dispatch and enabled synchronization stages.</summary>
public static class MeshShaderValidation
{
    /// <summary>Reads the required target-specific mesh metadata.</summary>
    /// <param name="data">The compiled mesh or task target.</param>
    /// <returns>The detached metadata.</returns>
    public static JsonElement Read(ShaderTargetData data) => MeshShaderMetadata.Read(data);

    /// <summary>Validates declared workgroup, payload and output requirements against enabled limits.</summary>
    /// <param name="data">The compiled stage.</param>
    /// <param name="caps">The enabled device capabilities.</param>
    public static void Validate(ShaderTargetData data, DeviceCaps caps)
    {
        JsonElement m = Read(data);
        GraphicsFeatures required = data.Stage == ShaderStage.Task ? GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader : GraphicsFeatures.MeshShader;
        if (data.Target != caps.ShaderTarget || (caps.Features & required) != required)
        {
            throw new NotSupportedException("The device does not support this mesh target or stage.");
        }

        ulong shared = m.GetProperty("sharedMemoryBytes").GetUInt64();
        ulong payload = m.GetProperty("payloadBytes").GetUInt64();
        ShaderWorkgroupLimits workgroup;
        if (data.Stage == ShaderStage.Task)
        {
            TaskShaderLimits limits = caps.TaskShaderLimits ?? throw new NotSupportedException("Task limits are unavailable.");
            workgroup = limits.Workgroup;
            Memory(shared, payload, limits.MaxSharedMemoryBytes, limits.MaxPayloadBytes, limits.MaxPayloadAndSharedMemoryBytes);
        }
        else
        {
            MeshShaderLimits limits = caps.MeshShaderLimits ?? throw new NotSupportedException("Mesh limits are unavailable.");
            workgroup = limits.Workgroup;
            Memory(shared, payload, limits.MaxSharedMemoryBytes, limits.MaxPayloadBytes, limits.MaxPayloadAndSharedMemoryBytes);
            ulong output = m.GetProperty("outputMemoryBytes").GetUInt64();
            if (m.GetProperty("outputTopology").GetString() == "point" ||
                m.GetProperty("maxOutputVertices").GetUInt32() > limits.MaxOutputVertices ||
                m.GetProperty("maxOutputPrimitives").GetUInt32() > limits.MaxOutputPrimitives ||
                output > limits.MaxOutputMemoryBytes || payload > limits.MaxPayloadAndOutputMemoryBytes || output > limits.MaxPayloadAndOutputMemoryBytes - payload)
            {
                throw new NotSupportedException("Mesh output exceeds enabled limits or uses unsupported point topology.");
            }
        }

        JsonElement size = m.GetProperty("workgroupSize");
        uint x = size[0].GetUInt32();
        uint y = size[1].GetUInt32();
        uint z = size[2].GetUInt32();
        if (x > workgroup.MaxSizeX || y > workgroup.MaxSizeY || z > workgroup.MaxSizeZ ||
            (ulong)x * y > workgroup.MaxInvocations / z)
        {
            throw new NotSupportedException("Shader local size exceeds enabled limits.");
        }
    }

    /// <summary>Validates task payload and mesh-to-fragment linkage including root schemas.</summary>
    /// <param name="mesh">The required mesh target.</param>
    /// <param name="task">The optional task target.</param>
    /// <param name="fragment">The optional fragment target.</param>
    public static void Program(ShaderTargetData mesh, ShaderTargetData? task, ShaderTargetData? fragment)
    {
        JsonElement m = Read(mesh);
        if (mesh.Stage != ShaderStage.Mesh || (task != null && task.Stage != ShaderStage.Task) || (fragment != null && fragment.Stage != ShaderStage.Fragment))
        {
            throw new ArgumentException("Mesh program stages do not match their slots.");
        }

        if (task != null)
        {
            JsonElement t = Read(task);
            if (t.GetProperty("payloadBytes").GetUInt64() != m.GetProperty("payloadBytes").GetUInt64() ||
                t.GetProperty("payloadAbi").GetString() != m.GetProperty("payloadAbi").GetString())
            {
                throw new ArgumentException("Task and Mesh payload ABI differs.");
            }
        }
        else if (m.GetProperty("payloadBytes").GetUInt64() != 0)
        {
            throw new ArgumentException("A mesh payload requires a task stage.");
        }

        ShaderTargetData[] stages = new[] { mesh, task, fragment }.OfType<ShaderTargetData>().ToArray();
        for (int i = 0; i < stages.Length; i++)
        {
            for (int j = i + 1; j < stages.Length; j++)
            {
                ShaderDataLayout.ValidateProgram(stages[i], stages[j]);
            }
        }

        if (stages.Any(stage => stage.Target != mesh.Target))
        {
            throw new ArgumentException("Program targets differ.");
        }

        if (fragment != null)
        {
            MeshShaderMetadata.ValidateFragmentIntegrity(fragment);
            using var doc = JsonDocument.Parse(fragment.ReflectionJson);
            if (!doc.RootElement.TryGetProperty("lumyteFragmentInputs", out JsonElement inputs))
            {
                throw new ArgumentException("Mesh linkage requires target-native Fragment signature metadata.");
            }

            foreach (JsonElement input in inputs.EnumerateArray())
            {
                if (input.TryGetProperty("semantic", out JsonElement semantic) && semantic.GetString()!.StartsWith("SV_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!m.GetProperty("outputs").EnumerateArray().Any(output => VaryingMatches(output, input, mesh.Target)))
                {
                    throw new ArgumentException("Mesh and Fragment target-native varying signatures differ.");
                }
            }
        }
    }

    /// <summary>Validates positive dispatch axes and their product without overflow.</summary>
    /// <param name="limits">The enabled stage dispatch limits.</param>
    /// <param name="x">The X group count.</param>
    /// <param name="y">The Y group count.</param>
    /// <param name="z">The Z group count.</param>
    public static void Dispatch(ShaderWorkgroupLimits limits, uint x, uint y, uint z)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (x == 0 || x > limits.MaxGroupCountX)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y == 0 || y > limits.MaxGroupCountY)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (z == 0 || z > limits.MaxGroupCountZ || (ulong)x * y > limits.MaxTotalGroupCount / z)
        {
            throw new ArgumentOutOfRangeException(nameof(z));
        }
    }

    /// <summary>Expands only current aggregate values and rejects explicitly unavailable mesh bits.</summary>
    /// <param name="stages">The caller's exact stage flags.</param>
    /// <param name="features">The enabled device features.</param>
    /// <returns>The available stage set; legacy aggregate numeric values remain unchanged.</returns>
    public static PipelineStage NormalizeStages(PipelineStage stages, GraphicsFeatures features)
    {
        PipelineStage unavailable = PipelineStage.None;
        if ((features & GraphicsFeatures.MeshShader) == 0)
        {
            unavailable |= PipelineStage.MeshShader;
        }

        if ((features & GraphicsFeatures.TaskShader) == 0)
        {
            unavailable |= PipelineStage.TaskShader;
        }

        if (stages is PipelineStage.AllGraphics or PipelineStage.AllCommands)
        {
            return stages & ~unavailable;
        }

        if ((stages & unavailable) != 0)
        {
            throw new NotSupportedException("Explicit synchronization stages require enabled Mesh/Task features.");
        }

        return stages;
    }

    private static void Memory(ulong shared, ulong payload, ulong maxShared, ulong maxPayload, ulong maxCombined)
    {
        if (shared > maxShared || payload > maxPayload || payload > maxCombined || shared > maxCombined - payload)
        {
            throw new NotSupportedException("Shader shared memory or payload exceeds enabled limits.");
        }
    }

    private static bool VaryingMatches(JsonElement output, JsonElement input, ShaderTarget target)
    {
        bool identity = target == ShaderTarget.Dxil
            ? output.TryGetProperty("semantic", out JsonElement a) && input.TryGetProperty("semantic", out JsonElement b) && string.Equals(a.GetString(), b.GetString(), StringComparison.OrdinalIgnoreCase)
            : output.GetProperty("location").ValueKind == JsonValueKind.Number && input.GetProperty("location").ValueKind == JsonValueKind.Number && output.GetProperty("location").GetUInt32() == input.GetProperty("location").GetUInt32();
        return identity && output.GetProperty("type").GetString() == input.GetProperty("type").GetString() &&
            (target == ShaderTarget.Dxil || output.GetProperty("perPrimitive").GetBoolean() == input.GetProperty("perPrimitive").GetBoolean()) &&
            output.GetProperty("interpolation").GetString() == input.GetProperty("interpolation").GetString();
    }
}
