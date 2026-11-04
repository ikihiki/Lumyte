using System.Text.Json.Serialization;

namespace Lumyte.Platform.Browser.TextInput;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserTextPacket[]))]

internal partial class BrowserTextJsonContext : JsonSerializerContext
{
}
