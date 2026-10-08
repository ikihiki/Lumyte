using System.Text.Json;

namespace Lumyte.Graphics.Wgpu;

internal sealed class MaterialSchema
{
    internal static MaterialSchema? Parse(string? reflection)
    {
        if (reflection is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(reflection);
        JsonElement parameters = doc.RootElement.GetProperty("parameters");
        if (parameters.GetArrayLength() != 9 || parameters[0].GetProperty("name").GetString() != "materials")
        {
            return null;
        }

        for (int i = 0; i < 9; i++)
        {
            JsonElement binding = parameters[i].GetProperty("binding");
            if (binding.GetProperty("kind").GetString() != "descriptorTableSlot" || binding.GetProperty("index").GetInt32() != i || (binding.TryGetProperty("space", out JsonElement space) && space.GetInt32() != 0))
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
        if (element.GetProperty("sizes")[0].GetProperty("value").GetInt32() != 32)
        {
            return null;
        }

        string[] names = ["baseColor", "textureSelector", "hasTexture", "padding0", "padding1"];
        int[] offsets = [0, 16, 20, 24, 28];
        JsonElement fields = element.GetProperty("fields");
        if (fields.GetArrayLength() != 5)
        {
            return null;
        }

        for (int i = 0; i < 5; i++)
        {
            if (fields[i].GetProperty("name").GetString() != names[i] || fields[i].GetProperty("binding").GetProperty("offset").GetInt32() != offsets[i])
            {
                return null;
            }
        }

        JsonElement colorType = fields[0].GetProperty("type");
        if (colorType.GetProperty("kind").GetString() != "vector" || colorType.GetProperty("elementCount").GetInt32() != 4 || colorType.GetProperty("elementType").GetProperty("scalarType").GetString() != "float32")
        {
            return null;
        }

        for (int i = 1; i < 5; i++)
        {
            if (fields[i].GetProperty("type").GetProperty("kind").GetString() != "scalar" || fields[i].GetProperty("type").GetProperty("scalarType").GetString() != "uint32")
            {
                return null;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (parameters[1 + (i * 2)].GetProperty("name").GetString() != $"materialTexture{i}" || parameters[2 + (i * 2)].GetProperty("name").GetString() != $"materialSampler{i}" || parameters[1 + (i * 2)].GetProperty("type").GetProperty("baseShape").GetString() != "texture2D" || parameters[2 + (i * 2)].GetProperty("type").GetProperty("kind").GetString() != "samplerState")
            {
                return null;
            }
        }

        return new();
    }
}
