using System.Text.Json;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class DocumentStage : IResourceSourceStage<GltfDocument>
{
    public async ValueTask<GltfDocument> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        SourceBytes bytes = await SourceBytes.ReadAsync(context, cancellationToken);
        var json = JsonDocument.Parse(bytes.Buffer.Memory);
        try
        {
            JsonElement root = json.RootElement;
            if (root.GetProperty("asset").GetProperty("version").GetString() != "2.0" ||
                (root.TryGetProperty("extensionsRequired", out JsonElement extensions) && extensions.GetArrayLength() != 0))
            {
                throw new NotSupportedException("The sample supports glTF 2.0 without required extensions.");
            }

            return new(context.Address.Id, json);
        }
        catch
        {
            json.Dispose();
            throw;
        }
    }

    public ValueTask ReleaseAsync(GltfDocument resource)
    {
        resource.Dispose();
        return ValueTask.CompletedTask;
    }
}
