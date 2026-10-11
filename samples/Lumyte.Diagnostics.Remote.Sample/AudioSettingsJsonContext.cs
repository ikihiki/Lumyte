using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Remote.Sample;

[JsonSerializable(typeof(AudioSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AudioSettingsJsonContext : JsonSerializerContext;
