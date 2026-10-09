using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.IntegrationTests;

[JsonSerializable(typeof(SettingsDiagnosticModel))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SettingsDiagnosticJsonContext : JsonSerializerContext;
