using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OldSnapshot))]
internal partial class OldWireContext : JsonSerializerContext
{
}
