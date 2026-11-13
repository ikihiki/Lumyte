using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal static class DrawRecordAbi2D
{
    internal static void Validate(ShaderArtifact artifact, ShaderTarget target, int mode = 3)
    {
        using var document = JsonDocument.Parse(artifact.GetTarget(target).ReflectionJson);
        JsonElement parameters = document.RootElement.GetProperty("parameters");
        Check<DrawRecord2D>(parameters, 96, [0, 4, 8, 12, 16, 32, 48, 64, 80, 84, 88, 92]);
        Check<DrawDirectoryEntry2D>(parameters, 16, [0, 4, 8, 12]);
        switch (mode)
        {
            case 0:
            case 4:
                Check<DrawBasicMetadata2D>(parameters, 48, [0, 16, 32]);
                break;
            case 1:
                Check<DrawMaskMetadata2D>(parameters, 64, [0, 16, 32, 48]);
                break;
            case 2:
            case 6:
            case 7:
                Check<DrawImageMetadata2D>(parameters, 64, [0, 16, 32, 48]);
                break;
            default:
                Check<DrawPageMetadata2D>(parameters, 80, [0, 16, 32, 48, 64]);
                break;
        }

        Check<MaskDescriptor2D>(parameters, 80, [0, 16, 32, 36, 40, 44, 48, 64]);
        Check<ImagePaintData2D>(parameters, 128, [0, 16, 32, 48, 64, 68, 72, 76, 80, 84, 88, 92, 96, 112]);
        if (mode == 4)
        {
            Check<SimpleDrawRangeArguments2D>(parameters, 64, [0, 16, 32, 36, 40, 48, 56, 60]);
        }
        else
        {
            Check<DrawRangeArguments2D>(parameters, 64, [0, 16, 32, 36, 40, 48, 56, 60]);
        }
    }

    private static void Check<T>(JsonElement parameters, int size, int[] offsets)
        where T : struct, IShaderArguments
    {
        JsonElement probe = parameters.EnumerateArray().Single(element => element.GetProperty("name").GetString() == "__lumyte_schema_" + T.ShaderTypeName);
        JsonElement pointer = probe.GetProperty("type").GetProperty("resultType");
        JsonElement type = pointer.TryGetProperty("valueType", out JsonElement value) ? value : pointer.GetProperty("elementType");
        int actualSize = type.GetProperty("sizes").EnumerateArray().Single(element => element.GetProperty("kind").GetString() == "uniform").GetProperty("value").GetInt32();
        ShaderValueSnapshot codec = IShaderArguments.Capture(default(T));
        JsonElement[] fields = type.GetProperty("fields").EnumerateArray().ToArray();
        if (actualSize != size || fields.Length != offsets.Length || codec.Values.Count != fields.Length)
        {
            throw new NotSupportedException("The shader target does not implement the canonical fixed draw ABI: " + T.ShaderTypeName);
        }

        for (int i = 0; i < fields.Length; i++)
        {
            JsonElement binding = fields[i].GetProperty("binding");
            if (fields[i].GetProperty("name").GetString() != codec.Values[i].Path || binding.GetProperty("offset").GetInt32() != offsets[i]
                || binding.GetProperty("size").GetInt32() != (codec.Values[i].IsReference ? 16 : codec.Values[i].Data.Length))
            {
                throw new NotSupportedException("The generated codec differs from the canonical fixed draw ABI: " + T.ShaderTypeName);
            }
        }
    }
}
