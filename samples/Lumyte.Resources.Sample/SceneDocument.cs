using System.Text.Json;

namespace Lumyte.Resources.Sample;

internal sealed class SceneDocument(JsonDocument document)
{
    internal JsonElement Root => document.RootElement;

    internal void Dispose() => document.Dispose();
}
