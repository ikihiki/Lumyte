using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.UI.Automation.Contracts;

/// <summary>Source-generated closed wire metadata; no UI or backend types are loaded.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(UiRemotePlan))]
[JsonSerializable(typeof(UiRemoteTarget[]))]
[JsonSerializable(typeof(UiRemoteJobStatus))]
[JsonSerializable(typeof(UiRemoteSnapshot))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int))]
public partial class UiAutomationJsonContext : JsonSerializerContext
{
}
