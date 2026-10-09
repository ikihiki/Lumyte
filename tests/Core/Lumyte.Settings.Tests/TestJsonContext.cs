using System.Text.Json.Serialization;

namespace Lumyte.Settings.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OtherSettings))]
[JsonSerializable(typeof(SampleSettings))]
[JsonSerializable(typeof(CollectionSettings))]
[JsonSerializable(typeof(ReadOnlySettings))]
[JsonSerializable(typeof(RecursiveSettings))]
internal partial class TestJsonContext : JsonSerializerContext;
