using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Lumyte.Graphics.Shaders;

internal static class MeshSpirVReflection
{
    internal static JsonArray Outputs(byte[] code) => SignatureVariables(code, 3);

    internal static JsonArray Inputs(byte[] code) => SignatureVariables(code, 1);

    private static JsonArray SignatureVariables(byte[] code, uint storage)
    {
        var types = new Dictionary<uint, uint[]>();
        var decorations = new Dictionary<uint, Dictionary<uint, uint>>();
        var variables = new List<(uint Type, uint Id)>();
        for (int offset = 20; offset < code.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            int words = checked((int)(header >> 16));
            uint opcode = header & 65535;
            if (words == 0 || words > (code.Length - offset) / 4)
            {
                throw new InvalidOperationException("Malformed compiler-generated SPIR-V instruction.");
            }

            uint[] operands = new uint[words - 1];
            for (int i = 0; i < operands.Length; i++)
            {
                operands[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + ((i + 1) * 4)));
            }

            if (opcode is >= 20 and <= 32)
            {
                types[operands[0]] = [opcode, .. operands.Skip(1)];
            }
            else if (opcode == 71)
            {
                if (!decorations.TryGetValue(operands[0], out Dictionary<uint, uint>? values))
                {
                    values = [];
                    decorations.Add(operands[0], values);
                }

                values[operands[1]] = operands.Length > 2 ? operands[2] : 1;
            }
            else if (opcode == 59 && operands[2] == storage)
            {
                variables.Add((operands[0], operands[1]));
            }

            offset += words * 4;
        }

        var outputs = new JsonArray();
        foreach ((uint type, uint id) in variables)
        {
            if (!decorations.TryGetValue(id, out Dictionary<uint, uint>? values) || values.ContainsKey(11))
            {
                continue;
            }

            if (!values.TryGetValue(30, out uint location))
            {
                throw new InvalidOperationException("Incomplete SPIR-V Mesh output location.");
            }

            string interpolation = values.ContainsKey(14) ? "nointerpolation" : values.ContainsKey(13) ? "noperspective" : "linear";
            interpolation += values.ContainsKey(16) ? "+centroid" : string.Empty;
            interpolation += values.ContainsKey(17) ? "+sample" : string.Empty;
            outputs.Add((JsonNode)new JsonObject
            {
                ["location"] = location,
                ["type"] = Signature(type, types),
                ["interpolation"] = interpolation,
                ["perPrimitive"] = values.ContainsKey(5271),
            });
        }

        return outputs;
    }

    private static string Signature(uint id, IReadOnlyDictionary<uint, uint[]> types)
    {
        uint[] type = types[id];
        return type[0] switch
        {
            20 => "bool",
            21 when type[1] == 32 => type[2] == 0 ? "uint" : "int",
            22 when type[1] == 32 => "float",
            23 => Signature(type[1], types) + type[2],
            28 => Signature(type[1], types),
            32 => Signature(type[2], types),
            _ => throw new InvalidOperationException("Unsupported SPIR-V Mesh varying type."),
        };
    }
}
