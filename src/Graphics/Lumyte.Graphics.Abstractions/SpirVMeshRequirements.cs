using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Lumyte.Graphics.Abstractions;

internal static class SpirVMeshRequirements
{
    internal static void Validate(byte[] code, JsonElement metadata)
    {
        var types = new Dictionary<uint, uint[]>();
        var constants = new Dictionary<uint, uint>();
        var variables = new List<(uint Type, uint Storage)>();
        for (int offset = 20; offset < code.Length;)
        {
            uint header = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset));
            int count = (int)(header >> 16);
            uint opcode = header & 65535;
            uint[] words = new uint[count - 1];
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.AsSpan(offset + ((i + 1) * 4)));
            }

            if (opcode is >= 20 and <= 32)
            {
                types[words[0]] = [opcode, .. words.Skip(1)];
            }
            else if (opcode == 43 && words.Length == 3)
            {
                constants[words[1]] = words[2];
            }
            else if (opcode == 59 && words.Length >= 3 && words[2] is 4 or 5402)
            {
                variables.Add((words[0], words[2]));
            }

            offset += count * 4;
        }

        ulong shared = 0;
        ulong payload = 0;
        string payloadAbi = string.Empty;
        foreach ((uint type, uint storage) in variables)
        {
            Layout value = Describe(type, types, constants, []);
            if (storage == 4)
            {
                shared = checked(shared + value.Bytes);
            }
            else
            {
                if (payload != 0)
                {
                    throw new ArgumentException("Multiple native task payload variables are unsupported.");
                }

                payload = value.Bytes;
                payloadAbi = value.Abi;
            }
        }

        if (shared > metadata.GetProperty("sharedMemoryBytes").GetUInt64() || payload != metadata.GetProperty("payloadBytes").GetUInt64() || payloadAbi != metadata.GetProperty("payloadAbi").GetString())
        {
            throw new ArgumentException("Native SPIR-V shared storage or payload ABI contradicts metadata.");
        }
    }

    private static Layout Describe(uint id, IReadOnlyDictionary<uint, uint[]> types, IReadOnlyDictionary<uint, uint> constants, HashSet<uint> active)
    {
        if (!types.TryGetValue(id, out uint[]? type) || !active.Add(id))
        {
            throw new ArgumentException("Incomplete or recursive native Mesh storage type.");
        }

        Layout result;
        switch (type[0])
        {
            case 20:
                result = new(4, 4, "bool1");
                break;
            case 21 when type[1] == 32:
                result = new(4, 4, type[2] == 0 ? "uint1" : "int1");
                break;
            case 22 when type[1] == 32:
                result = new(4, 4, "float1");
                break;
            case 23:
                Layout scalar = Describe(type[1], types, constants, active);
                uint lanes = type[2];
                result = new(checked(scalar.Bytes * lanes), lanes == 3 ? 16u : 4u * lanes, scalar.Abi[..^1] + lanes.ToString(CultureInfo.InvariantCulture));
                break;
            case 28:
                Layout element = Describe(type[1], types, constants, active);
                if (!constants.TryGetValue(type[2], out uint count))
                {
                    throw new ArgumentException("Missing native Mesh storage array size.");
                }

                result = element with { Bytes = checked(Align(element.Bytes, element.Alignment) * count), Count = count };
                break;
            case 30:
                ulong bytes = 0;
                uint alignment = 4;
                var fields = new List<string>();
                foreach (uint member in type.Skip(1))
                {
                    Layout field = Describe(member, types, constants, active);
                    alignment = Math.Max(alignment, field.Alignment);
                    bytes = Align(bytes, field.Alignment);
                    fields.Add(bytes.ToString(CultureInfo.InvariantCulture) + ":" + field.Abi + "[" + field.Count.ToString(CultureInfo.InvariantCulture) + "]");
                    bytes = checked(bytes + field.Bytes);
                }

                bytes = Align(bytes, alignment);
                result = new(bytes, alignment, "{" + string.Join(';', fields) + "}:" + bytes.ToString(CultureInfo.InvariantCulture));
                break;
            case 32:
                result = Describe(type[2], types, constants, active);
                break;
            default:
                throw new ArgumentException("Unsupported native Mesh storage type.");
        }

        active.Remove(id);
        return result;
    }

    private static ulong Align(ulong value, uint alignment) => checked(((value + alignment - 1) / alignment) * alignment);

    private sealed record Layout(ulong Bytes, uint Alignment, string Abi, uint Count = 1);
}
