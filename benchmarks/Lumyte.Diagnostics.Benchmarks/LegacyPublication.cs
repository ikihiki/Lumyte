using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.MagicOnion;
using MessagePack;
using MessagePack.Resolvers;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Keeps the superseded publication DTO path solely as a benchmark baseline.</summary>
internal static class LegacyPublication
{
    public static MessagePackSerializerOptions Options { get; } = DiagnosticMessagePack.Options.WithResolver(
        CompositeResolver.Create(BenchmarkMessagePackResolver.Instance, DiagnosticMessagePackResolver.Instance, StandardResolver.Instance));

    public static LegacyPublicationMessage Map(DiagnosticMessage value) => new()
    {
        MessageId = value.MessageId.ToString("D"),
        SessionId = value.SessionId.ToString("D"),
        Kind = (int)value.Kind,
        RequestId = value.RequestId?.ToString("D"),
        Result = value.Result == null ? null : new() { Status = value.Result.Status, Values = value.Result.Values == null ? null : Values(value.Result.Values), Revision = value.Result.Revision, Code = value.Result.Code, Message = value.Result.Message },
        Events = value.Events.Select(item => new LegacyPublicationEvent { Kind = item.Kind, Timestamp = item.Timestamp, Name = item.Name, Value = Scalar(item.Value), TraceId = item.TraceId, SpanId = item.SpanId, ParentSpanId = item.ParentSpanId, DurationTicks = item.DurationTicks, Fields = Values(item.Fields) }).ToArray(),
    };

    public static DiagnosticMessage Map(LegacyPublicationMessage value) => new(Guid.Parse(value.MessageId), Guid.Parse(value.SessionId), (DiagnosticMessageKind)value.Kind, value.RequestId == null ? null : Guid.Parse(value.RequestId), value.Result == null ? null : new(value.Result.Status, value.Result.Values == null ? null : Values(value.Result.Values), value.Result.Revision, value.Result.Code, value.Result.Message), value.Events.Select(item => new DiagnosticEvent(item.Kind, item.Timestamp, item.Name, Scalar(item.Value), item.TraceId, item.SpanId, item.ParentSpanId, item.DurationTicks, Values(item.Fields))).ToArray());

    private static LegacyPublicationValue Scalar(DiagnosticValue value) => new() { Kind = (int)value.Kind, Boolean = value.Boolean, Integer = value.Int64, Number = value.Double, Text = value.String };

    private static DiagnosticValue Scalar(LegacyPublicationValue value) => new((DiagnosticValueKind)value.Kind, value.Boolean, value.Integer, value.Number, value.Text);

    private static Dictionary<string, LegacyPublicationValue> Values(IReadOnlyDictionary<string, DiagnosticValue> values) => values.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal);

    private static Dictionary<string, DiagnosticValue> Values(Dictionary<string, LegacyPublicationValue> values) => values.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal);
}
