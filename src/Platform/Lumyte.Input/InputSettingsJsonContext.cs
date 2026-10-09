using System.Text.Json.Serialization;

namespace Lumyte.Input;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(InputSettings))]
internal partial class InputSettingsJsonContext : JsonSerializerContext;
