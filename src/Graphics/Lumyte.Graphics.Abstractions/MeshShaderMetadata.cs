using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Validates compiler-owned mesh requirements without owning native resources.</summary>
public static class MeshShaderMetadata
{
    /// <summary>Reads the required versioned target metadata, rejecting missing or contradictory declarations.</summary>
    /// <param name="data">The compiled target and reflection.</param>
    /// <returns>A detached validated metadata object.</returns>
    public static JsonElement Read(ShaderTargetData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            using var document = JsonDocument.Parse(data.ReflectionJson);
            JsonElement root = document.RootElement;
            JsonElement metadata = root.GetProperty("lumyteMesh");
            string expected = data.Stage switch
            {
                ShaderStage.Mesh => "mesh",
                ShaderStage.Task => "task",
                _ => throw new ArgumentException("Mesh metadata requires a Mesh or Task stage.", nameof(data)),
            };
            if (data.Target == ShaderTarget.Wgsl || metadata.GetProperty("version").GetInt32() != 1 ||
                metadata.GetProperty("stage").GetString() != expected || metadata.GetProperty("entryPoint").GetString() != data.EntryPoint)
            {
                throw new ArgumentException("Mesh compilation metadata contradicts the artifact.", nameof(data));
            }

            JsonElement entry = root.GetProperty("entryPoints").EnumerateArray().Single(e => e.GetProperty("name").GetString() == data.EntryPoint);
            string? reflectedStage = entry.GetProperty("stage").GetString();
            if (reflectedStage != (data.Stage == ShaderStage.Task ? "amplification" : "mesh"))
            {
                throw new ArgumentException("Reflected entry stage contradicts the artifact.", nameof(data));
            }

            GraphicsFeatures required = data.Stage == ShaderStage.Task ? GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader : GraphicsFeatures.MeshShader;
            if (metadata.GetProperty("requiredFeatures").GetUInt32() != (uint)required)
            {
                throw new ArgumentException("Invalid Mesh/Task feature requirements.", nameof(data));
            }

            JsonElement size = metadata.GetProperty("workgroupSize");
            if (size.GetArrayLength() != 3 || size.EnumerateArray().Any(value => value.GetUInt32() == 0))
            {
                throw new ArgumentException("Mesh workgroup dimensions must be positive.", nameof(data));
            }

            _ = metadata.GetProperty("sharedMemoryBytes").GetUInt64();
            ulong payload = metadata.GetProperty("payloadBytes").GetUInt64();
            string? payloadAbi = metadata.GetProperty("payloadAbi").GetString();
            if (payloadAbi == null || (payload != 0 && payloadAbi.Length == 0) || (payload == 0 && payloadAbi.Length != 0))
            {
                throw new ArgumentException("Missing payload ABI.", nameof(data));
            }

            if (data.Stage == ShaderStage.Mesh)
            {
                if (metadata.GetProperty("maxOutputVertices").GetUInt32() == 0 || metadata.GetProperty("maxOutputPrimitives").GetUInt32() == 0 ||
                    metadata.GetProperty("outputTopology").GetString() is not ("line" or "triangle" or "point") || metadata.GetProperty("outputs").ValueKind != JsonValueKind.Array)
                {
                    throw new ArgumentException("Invalid mesh output declarations.", nameof(data));
                }

                if (metadata.GetProperty("outputMemoryBytes").GetUInt64() == 0)
                {
                    throw new ArgumentException("Mesh output memory requirements must be positive.", nameof(data));
                }

                foreach (JsonElement output in metadata.GetProperty("outputs").EnumerateArray())
                {
                    if (string.IsNullOrWhiteSpace(output.GetProperty("type").GetString()) || string.IsNullOrWhiteSpace(output.GetProperty("interpolation").GetString()))
                    {
                        throw new ArgumentException("Incomplete Mesh varying ABI.", nameof(data));
                    }

                    _ = output.GetProperty("perPrimitive").GetBoolean();
                    JsonElement location = output.GetProperty("location");
                    if (location.ValueKind != JsonValueKind.Null)
                    {
                        _ = location.GetUInt32();
                    }
                    else if (!output.GetProperty("semantic").GetString()!.StartsWith("SV_", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Missing varying identity.", nameof(data));
                    }
                }
            }

            if (data.Code.Length != 0)
            {
                string expectedIntegrity = Integrity(data.Code, root);
                if (!root.TryGetProperty("lumyteMeshIntegrity", out JsonElement integrity) || !string.Equals(integrity.GetString(), expectedIntegrity, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Mesh code and requirement metadata integrity differs.", nameof(data));
                }
            }

            if (data.Target == ShaderTarget.SpirV)
            {
                ValidateSpirV(data, metadata);
                SpirVMeshRequirements.Validate(data.Code, metadata);
            }

            if (data.Target == ShaderTarget.Dxil && data.Code.Length != 0)
            {
                ValidateDxil(data, metadata);
            }

            return metadata.Clone();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException or DivideByZeroException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Incomplete or invalid Mesh/Task reflection.", nameof(data), error);
        }
    }

    /// <summary>Binds compiler-derived requirements to their exact target code, detecting inconsistent artifact edits.</summary>
    /// <param name="reflection">The completed compiler reflection.</param>
    /// <param name="code">The matching native target code.</param>
    /// <returns>The reflection with an integrity commitment; this detects corruption, not author authenticity.</returns>
    public static string CompleteIntegrity(string reflection, ReadOnlySpan<byte> code)
    {
        JsonObject root = JsonNode.Parse(reflection)!.AsObject();
        using var document = JsonDocument.Parse(reflection);
        root["lumyteMeshIntegrity"] = Integrity(code, document.RootElement);
        return root.ToJsonString();
    }

    /// <summary>Binds compiler-derived native Fragment signatures and root reflection to their code.</summary>
    /// <param name="reflection">The completed Fragment reflection.</param>
    /// <param name="code">The matching target code.</param>
    /// <returns>The reflection with a corruption-detection commitment.</returns>
    public static string CompleteFragmentIntegrity(string reflection, ReadOnlySpan<byte> code)
    {
        JsonObject root = JsonNode.Parse(reflection)!.AsObject();
        using var document = JsonDocument.Parse(reflection);
        root["lumyteFragmentIntegrity"] = Integrity(code, document.RootElement);
        return root.ToJsonString();
    }

    /// <summary>Checks native Fragment signature/reflection consistency without requiring metadata in legacy conventional artifacts.</summary>
    /// <param name="data">The compiled Fragment target.</param>
    public static void ValidateFragmentIntegrity(ShaderTargetData data)
    {
        using var document = JsonDocument.Parse(data.ReflectionJson);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("lumyteFragmentInputs", out _) &&
            (!root.TryGetProperty("lumyteFragmentIntegrity", out JsonElement integrity) || integrity.GetString() != Integrity(data.Code, root)))
        {
            throw new ArgumentException("Native Fragment code and reflection integrity differs.", nameof(data));
        }
    }

    private static string Integrity(ReadOnlySpan<byte> code, JsonElement metadata)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(code);
        JsonObject normalized = JsonNode.Parse(metadata.GetRawText())!.AsObject();
        normalized.Remove("lumyteMeshIntegrity");
        normalized.Remove("lumyteFragmentIntegrity");
        hash.AppendData(Encoding.UTF8.GetBytes(normalized.ToJsonString()));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ValidateDxil(ShaderTargetData data, JsonElement metadata)
    {
        ReadOnlySpan<byte> code = data.Code;
        if (code.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x43425844 || BinaryPrimitives.ReadUInt32LittleEndian(code[24..]) != code.Length)
        {
            throw new ArgumentException("Malformed DXIL container.", nameof(data));
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(code[28..]);
        if (count > (code.Length - 32) / 4)
        {
            throw new ArgumentException("Malformed DXIL part table.", nameof(data));
        }

        uint stage = data.Stage == ShaderStage.Mesh ? 13u : 14u;
        bool program = false;
        bool requirements = false;
        for (int i = 0; i < count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(code[(32 + (i * 4))..]);
            if (offset > code.Length - 8)
            {
                throw new ArgumentException("Malformed DXIL part offset.", nameof(data));
            }

            ReadOnlySpan<byte> part = code[(int)offset..];
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(part[4..]);
            if (length > part.Length - 8)
            {
                throw new ArgumentException("Truncated DXIL part.", nameof(data));
            }

            uint kind = BinaryPrimitives.ReadUInt32LittleEndian(part);
            ReadOnlySpan<byte> payload = part.Slice(8, (int)length);
            if (kind == 0x4C495844)
            {
                if (program || payload.Length < 24 || BinaryPrimitives.ReadUInt32LittleEndian(payload) >> 16 != stage)
                {
                    throw new ArgumentException("DXIL shader stage contradicts metadata.", nameof(data));
                }

                program = true;
            }
            else if (kind == 0x30565350)
            {
                if (requirements || payload.Length < 52)
                {
                    throw new ArgumentException("Missing DXIL Mesh runtime requirements.", nameof(data));
                }

                uint size = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                if (size < 48 || size > payload.Length - 4)
                {
                    throw new ArgumentException("Unsupported DXIL runtime requirement layout.", nameof(data));
                }

                ReadOnlySpan<byte> info = payload.Slice(4, (int)size);
                JsonElement local = metadata.GetProperty("workgroupSize");
                for (int axis = 0; axis < 3; axis++)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(info[(36 + (axis * 4))..]) != local[axis].GetUInt32())
                    {
                        throw new ArgumentException("DXIL local size contradicts metadata.", nameof(data));
                    }
                }

                ulong bytes = metadata.GetProperty("payloadBytes").GetUInt64();
                uint nativePayload = BinaryPrimitives.ReadUInt32LittleEndian(info[(data.Stage == ShaderStage.Mesh ? 8 : 0)..]);
                if (info[24] != stage || nativePayload != bytes)
                {
                    throw new ArgumentException("DXIL payload requirements contradict metadata.", nameof(data));
                }

                if (data.Stage == ShaderStage.Mesh &&
                    (BinaryPrimitives.ReadUInt32LittleEndian(info) > metadata.GetProperty("sharedMemoryBytes").GetUInt64() ||
                    BinaryPrimitives.ReadUInt16LittleEndian(info[12..]) != metadata.GetProperty("maxOutputVertices").GetUInt32() ||
                    BinaryPrimitives.ReadUInt16LittleEndian(info[14..]) != metadata.GetProperty("maxOutputPrimitives").GetUInt32() ||
                    info[27] != (metadata.GetProperty("outputTopology").GetString() == "line" ? 1 : 2)))
                {
                    throw new ArgumentException("DXIL Mesh requirements contradict metadata.", nameof(data));
                }

                requirements = true;
            }
        }

        if (!program || !requirements)
        {
            throw new ArgumentException("Missing DXIL executable or runtime requirements.", nameof(data));
        }
    }

    private static void ValidateSpirV(ShaderTargetData data, JsonElement metadata)
    {
        byte[] code = data.Code;
        if (code.Length < 20 || code.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x07230203)
        {
            throw new ArgumentException("Invalid Mesh/Task SPIR-V code.", nameof(data));
        }

        uint entryId = 0;
        var modes = new Dictionary<uint, uint[]>();
        for (int offset = 20; offset < code.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            int count = checked((int)(header >> 16));
            uint opcode = header & 65535;
            if (count == 0 || count > (code.Length - offset) / 4)
            {
                throw new ArgumentException("Truncated SPIR-V instruction.", nameof(data));
            }

            uint[] words = new uint[count];
            for (int i = 1; i < count; i++)
            {
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + (i * 4)));
            }

            if (opcode == 15 && count >= 4)
            {
                ReadOnlySpan<byte> nameBytes = code.AsSpan(offset + 12, (count - 3) * 4);
                int end = nameBytes.IndexOf((byte)0);
                if (end < 0)
                {
                    throw new ArgumentException("Invalid SPIR-V entry name.", nameof(data));
                }

                if (Encoding.UTF8.GetString(nameBytes[..end]) == data.EntryPoint)
                {
                    uint expected = data.Stage == ShaderStage.Mesh ? 5365u : 5364u;
                    if (words[1] != expected || entryId != 0)
                    {
                        throw new ArgumentException("SPIR-V execution model contradicts Mesh/Task metadata.", nameof(data));
                    }

                    entryId = words[2];
                }
            }

            offset += count * 4;
        }

        if (entryId == 0)
        {
            throw new ArgumentException("SPIR-V entry is missing.", nameof(data));
        }

        for (int offset = 20; offset < code.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            int count = (int)(header >> 16);
            if ((header & 65535) == 16 && count >= 3 && BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + 4)) == entryId)
            {
                uint mode = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + 8));
                uint[] values = new uint[count - 3];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + ((i + 3) * 4)));
                }

                modes.Add(mode, values);
            }

            offset += count * 4;
        }

        uint[] size = metadata.GetProperty("workgroupSize").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
        if (!modes.TryGetValue(17, out uint[]? local) || !local.SequenceEqual(size))
        {
            throw new ArgumentException("SPIR-V local size contradicts metadata.", nameof(data));
        }

        if (data.Stage == ShaderStage.Mesh)
        {
            uint topology = metadata.GetProperty("outputTopology").GetString() switch { "point" => 27u, "line" => 5269u, _ => 5298u };
            if (!modes.TryGetValue(26, out uint[]? vertices) || vertices.Length != 1 || vertices[0] != metadata.GetProperty("maxOutputVertices").GetUInt32() ||
                !modes.TryGetValue(5270, out uint[]? primitives) || primitives.Length != 1 || primitives[0] != metadata.GetProperty("maxOutputPrimitives").GetUInt32() || !modes.ContainsKey(topology))
            {
                throw new ArgumentException("SPIR-V output declarations contradict metadata.", nameof(data));
            }
        }
    }
}
