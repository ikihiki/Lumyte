using System.Text.Json;

namespace Lumyte.Graphics.Wgpu;

internal sealed class MaterialSchema
{
    internal MaterialSchema(ulong elementStrideInBytes, IReadOnlyDictionary<string, ShaderDataField> fields)
    {
        ElementStrideInBytes = elementStrideInBytes;
        Fields = fields;
    }

    internal ulong ElementStrideInBytes { get; }

    internal IReadOnlyDictionary<string, ShaderDataField> Fields { get; }

    internal static MaterialSchema? Parse(string? reflection)
    {
        if (reflection is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(reflection);
        JsonElement reflectedParameters = doc.RootElement.GetProperty("parameters");
        if (reflectedParameters.GetArrayLength() != 9)
        {
            return null;
        }

        JsonElement[] parameters = reflectedParameters.EnumerateArray()
            .OrderBy(parameter => parameter.GetProperty("binding").GetProperty("index").GetInt32()).ToArray();
        if (parameters[0].GetProperty("name").GetString() != "materials")
        {
            return null;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            JsonElement binding = parameters[i].GetProperty("binding");
            if (binding.GetProperty("kind").GetString() != "descriptorTableSlot" || binding.GetProperty("index").GetInt32() != i ||
                (binding.TryGetProperty("space", out JsonElement space) && space.GetInt32() != 0))
            {
                return null;
            }
        }

        JsonElement type = parameters[0].GetProperty("type");
        if (type.GetProperty("kind").GetString() != "resource" || type.GetProperty("baseShape").GetString() != "structuredBuffer")
        {
            return null;
        }

        JsonElement element = type.GetProperty("resultType");
        if (element.GetProperty("kind").GetString() != "struct")
        {
            return null;
        }

        int stride = element.GetProperty("sizes")[0].GetProperty("value").GetInt32();
        if (stride <= 0 || stride % 4 != 0)
        {
            return null;
        }

        var fields = new Dictionary<string, ShaderDataField>(StringComparer.Ordinal);
        foreach (JsonElement field in element.GetProperty("fields").EnumerateArray())
        {
            JsonElement fieldType = field.GetProperty("type");
            string? kind = fieldType.GetProperty("kind").GetString();
            int count = kind == "vector" ? fieldType.GetProperty("elementCount").GetInt32() : 1;
            if (kind is not ("scalar" or "vector") || count < 1 || count > 4)
            {
                return null;
            }

            string? scalarType = (kind == "vector" ? fieldType.GetProperty("elementType") : fieldType).GetProperty("scalarType").GetString();
            if (scalarType is not ("float32" or "uint32" or "int32"))
            {
                return null;
            }

            JsonElement fieldBinding = field.GetProperty("binding");
            int offset = fieldBinding.GetProperty("offset").GetInt32();
            int size = fieldBinding.GetProperty("size").GetInt32();
            if (offset < 0 || offset % 4 != 0 || size != count * 4 || offset > stride - size ||
                fields.Values.Any(existing => existing.Offset < offset + size && offset < existing.Offset + existing.Size))
            {
                return null;
            }

            string name = field.GetProperty("name").GetString()!;
            if (!fields.TryAdd(name, new(offset, size, scalarType, count)))
            {
                return null;
            }
        }

        if (fields.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < 4; i++)
        {
            if (parameters[1 + (i * 2)].GetProperty("name").GetString() != $"materialTexture{i}" ||
                parameters[2 + (i * 2)].GetProperty("name").GetString() != $"materialSampler{i}" ||
                parameters[1 + (i * 2)].GetProperty("type").GetProperty("baseShape").GetString() != "texture2D" ||
                parameters[2 + (i * 2)].GetProperty("type").GetProperty("kind").GetString() != "samplerState")
            {
                return null;
            }
        }

        return new((ulong)stride, fields);
    }
}
