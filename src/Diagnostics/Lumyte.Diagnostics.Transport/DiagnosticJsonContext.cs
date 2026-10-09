using System.Text.Json.Serialization;

namespace Lumyte.Diagnostics.Transport;

/// <summary>Generated metadata for the closed initial protocol.</summary>
[JsonSerializable(typeof(ClientHello))]
[JsonSerializable(typeof(SessionWelcome))]
[JsonSerializable(typeof(DiagnosticCommand))]
[JsonSerializable(typeof(OperationInvocation))]
[JsonSerializable(typeof(DiagnosticMessage))]
[JsonSerializable(typeof(PublishReceipt))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(DiagnosticCommand[]))]
[JsonSerializable(typeof(SessionSnapshot[]))]
[JsonSerializable(typeof(DiagnosticEvent[]))]
[JsonSerializable(typeof(DiagnosticOperationResult))]
public partial class DiagnosticJsonContext : JsonSerializerContext
{
}
