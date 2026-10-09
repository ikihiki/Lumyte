using System.Text.Json.Serialization;

namespace Lumyte.Settings.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OtherSettings))]
[JsonSerializable(typeof(SampleSettings))]
internal partial class TestJsonContext : JsonSerializerContext;
