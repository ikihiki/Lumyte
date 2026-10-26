using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Server;

[JsonSerializable(typeof(DiagnosticUiBootstrap))]
internal partial class DiagnosticUiJsonContext : JsonSerializerContext
{
    internal static DiagnosticUiJsonContext Protocol { get; } = new(new JsonSerializerOptions(DiagnosticJson.Context.Options));
}
