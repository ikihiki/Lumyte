using System.Text.Json.Serialization;

namespace Lumyte.Settings.Tests;

[JsonSerializable(typeof(RequiredPropertiesTests.NestedSettings))]
[JsonSerializable(typeof(RequiredPropertiesTests.RequiredSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class RequiredPropertiesJsonContext : JsonSerializerContext;
