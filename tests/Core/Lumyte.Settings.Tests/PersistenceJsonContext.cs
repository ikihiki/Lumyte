using System.Text.Json.Serialization;

namespace Lumyte.Settings.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PersistenceJsonTests.Values))]
internal partial class PersistenceJsonContext : JsonSerializerContext;
