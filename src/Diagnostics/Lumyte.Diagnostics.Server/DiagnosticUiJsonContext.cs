using System.Text.Json;
using System.Text.Json.Serialization;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

[JsonSerializable(typeof(DiagnosticUiBootstrap))]
[JsonSerializable(typeof(DiagnosticUiState))]
internal partial class DiagnosticUiJsonContext : JsonSerializerContext
{
    internal static DiagnosticUiJsonContext Protocol { get; } = new(new JsonSerializerOptions(DiagnosticJson.Context.Options));
}
