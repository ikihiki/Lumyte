using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Lumyte.Graphics.Shaders;

internal static class MeshSpirVReflection
{
    internal static JsonArray Outputs(byte[] code) => SignatureVariables(code, 3);

    internal static JsonArray Inputs(byte[] code) => SignatureVariables(code, 1);

    private static JsonArray SignatureVariables(byte[] code, uint storage)
    {
        if (code.Length < 20 || code.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(code) != 0x07230203)
        {
            throw Invalid();
        }

        var types = new Dictionary<uint, uint[]>();
        var constants = new Dictionary<uint, (uint Type, uint[] Words)>();
        var decorations = new Dictionary<uint, Dictionary<uint, uint>>();
        var variables = new List<(uint Type, uint Id)>();
        var memberDecoratedTypes = new HashSet<uint>();
        for (int offset = 20; offset < code.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            int words = checked((int)(header >> 16));
            uint opcode = header & 65535;
            if (words == 0 || words > (code.Length - offset) / 4)
            {
                throw Invalid();
            }

            uint[] operands = new uint[words - 1];
            for (int i = 0; i < operands.Length; i++)
            {
                operands[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + ((i + 1) * 4)));
            }

            if (opcode is >= 20 and <= 32)
            {
                if (operands.Length < 1 || !types.TryAdd(operands[0], [opcode, .. operands.Skip(1)]))
                {
                    throw Invalid();
                }
            }
            else if (opcode == 43)
            {
                if (operands.Length < 3 || !constants.TryAdd(operands[1], (operands[0], operands[2..])))
                {
                    throw Invalid();
                }
            }
            else if (opcode == 71)
            {
                if (operands.Length < 2 || (operands[1] is 11 or 30 or 31 && operands.Length != 3))
                {
                    throw Invalid();
                }

                if (!decorations.TryGetValue(operands[0], out Dictionary<uint, uint>? values))
                {
                    values = [];
                    decorations.Add(operands[0], values);
                }

                if (!values.TryAdd(operands[1], operands.Length > 2 ? operands[2] : 1))
                {
                    throw Invalid();
                }
            }
            else if (opcode == 72)
            {
                if (operands.Length < 3)
                {
                    throw Invalid();
                }

                if (operands[2] is 11 or 13 or 14 or 16 or 17 or 30 or 31 or 5271)
                {
                    memberDecoratedTypes.Add(operands[0]);
                }
            }
            else if (opcode == 59)
            {
                if (operands.Length < 3)
                {
                    throw Invalid();
                }

                if (operands[2] == storage)
                {
                    variables.Add((operands[0], operands[1]));
                }
            }

            offset += words * 4;
        }

        var outputs = new JsonArray();
        var occupied = new HashSet<uint>();
        foreach ((uint type, uint id) in variables)
        {
            if (!decorations.TryGetValue(id, out Dictionary<uint, uint>? values))
            {
                throw Invalid();
            }

            // Built-ins have a separate interface and do not consume user locations.
            if (values.ContainsKey(11))
            {
                continue;
            }

            if (!values.TryGetValue(30, out uint location) || values.ContainsKey(31))
            {
                throw Invalid();
            }

            uint[] pointer = Type(type);
            if (pointer.Length != 3 || pointer[0] != 32 || pointer[1] != storage)
            {
                throw Invalid();
            }

            uint element = pointer[2];
            if (storage == 3)
            {
                // Mesh outputs have exactly one implicit vertex/primitive dimension.
                // All remaining array extents belong to the varying itself.
                uint[] outer = Type(element);
                if (outer.Length != 3 || outer[0] != 28)
                {
                    throw Invalid();
                }

                _ = ArrayLength(outer[2]);
                element = outer[1];
            }

            string interpolation = values.ContainsKey(14) ? "nointerpolation" : values.ContainsKey(13) ? "noperspective" : "linear";
            interpolation += values.ContainsKey(16) ? "+centroid" : string.Empty;
            interpolation += values.ContainsKey(17) ? "+sample" : string.Empty;
            Expand(element, new HashSet<uint>());

            void Expand(uint current, HashSet<uint> visiting)
            {
                if (visiting.Count >= 64 || !visiting.Add(current))
                {
                    throw Invalid();
                }

                uint[] definition = Type(current);
                if (definition[0] == 30)
                {
                    // Slang can lower matrix storage to a struct containing a vector
                    // array. Member-level interface overrides need separate handling.
                    if (definition.Length < 2 || memberDecoratedTypes.Contains(current))
                    {
                        throw Invalid();
                    }

                    foreach (uint member in definition.Skip(1))
                    {
                        Expand(member, visiting);
                    }
                }
                else if (definition[0] is 28 or 24)
                {
                    if (definition.Length != 3)
                    {
                        throw Invalid();
                    }

                    uint count = definition[0] == 28 ? ArrayLength(definition[2]) : definition[2];
                    if (definition[0] == 24 && (count is < 2 or > 4 || Type(definition[1])[0] != 23))
                    {
                        throw Invalid();
                    }

                    for (uint i = 0; i < count; i++)
                    {
                        Expand(definition[1], visiting);
                    }
                }
                else
                {
                    // Bound expansion, reject overlapping or overflowing locations, and
                    // retain the native vector type for linkage and Vulkan accounting.
                    if (outputs.Count >= 65536 || !occupied.Add(location))
                    {
                        throw Invalid();
                    }

                    outputs.Add((JsonNode)new JsonObject
                    {
                        ["location"] = location,
                        ["type"] = Signature(current),
                        ["interpolation"] = interpolation,
                        ["perPrimitive"] = values.ContainsKey(5271),
                    });
                    location = checked(location + 1);
                }

                visiting.Remove(current);
            }
        }

        return outputs;

        uint[] Type(uint id) => types.TryGetValue(id, out uint[]? value) ? value : throw Invalid();

        uint ArrayLength(uint id)
        {
            if (!constants.TryGetValue(id, out (uint Type, uint[] Words) value))
            {
                throw Invalid();
            }

            uint[] integer = Type(value.Type);
            if (integer.Length != 3 || integer[0] != 21 || integer[1] != 32 || value.Words.Length != 1 ||
                value.Words[0] == 0 || value.Words[0] > 65536)
            {
                throw Invalid();
            }

            return value.Words[0];
        }

        string Signature(uint id)
        {
            uint[] definition = Type(id);
            return definition[0] switch
            {
                20 when definition.Length == 1 => "bool",
                21 when definition.Length == 3 && definition[1] == 32 && definition[2] <= 1 => definition[2] == 0 ? "uint" : "int",
                22 when definition.Length == 2 && definition[1] == 32 => "float",
                23 when definition.Length == 3 && definition[2] is >= 2 and <= 4 && Type(definition[1])[0] is 20 or 21 or 22 => Signature(definition[1]) + definition[2],
                _ => throw Invalid(),
            };
        }
    }

    private static InvalidOperationException Invalid() => new("Malformed or unsupported SPIR-V varying signature.");
}
