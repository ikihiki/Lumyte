using System.Text.Json.Serialization;

namespace Lumyte.Platform.Browser;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserInputEvent[]))]
internal partial class BrowserInputJsonContext : JsonSerializerContext;
