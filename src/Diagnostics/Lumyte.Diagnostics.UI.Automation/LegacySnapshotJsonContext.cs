using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.UI.Automation;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(LegacyRemoteSnapshot))]
[JsonSerializable(typeof(SelectionRemoteSnapshot))]
[JsonSerializable(typeof(NumericRemoteSnapshot))]
[JsonSerializable(typeof(ExpandedRemoteSnapshot))]
[JsonSerializable(typeof(TreeRemoteSnapshot))]
internal partial class LegacySnapshotJsonContext : JsonSerializerContext
{
}
