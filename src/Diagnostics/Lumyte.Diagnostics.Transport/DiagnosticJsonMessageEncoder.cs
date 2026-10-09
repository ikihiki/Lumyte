using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Lumyte.Diagnostics.Transport;

/// <summary>Writes game publications directly without per-event or per-field transport DTOs.</summary>
public static class DiagnosticJsonMessageEncoder
{
    /// <summary>Writes the version-one JSON message into caller-owned storage.</summary>
    /// <param name="destination">The buffer, owned by the caller.</param>
    /// <param name="message">The detached publication snapshot.</param>
    public static void Write(IBufferWriter<byte> destination, DiagnosticMessage message)
    {
        using var writer = new Utf8JsonWriter(destination);
        Write(writer, message);
        writer.Flush();
    }

    /// <summary>Writes one message and leaves the supplied writer open.</summary>
    /// <param name="writer">The synchronous JSON writer.</param>
    /// <param name="message">The publication snapshot.</param>
    public static void Write(Utf8JsonWriter writer, DiagnosticMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("messageId", message.MessageId);
        writer.WriteString("sessionId", message.SessionId);
        writer.WriteNumber("kind", (int)message.Kind);
        writer.WriteString("requestId", message.RequestId?.ToString("D"));
        writer.WritePropertyName("result");
        WriteResult(writer, message.Result);
        writer.WriteStartArray("events");
        foreach (DiagnosticEvent item in message.Events)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", item.Kind);
            WriteInteger(writer, "timestamp", item.Timestamp);
            writer.WriteString("name", item.Name);
            writer.WritePropertyName("value");
            WriteScalar(writer, item.Value.Kind, item.Value.Boolean, item.Value.Int64, item.Value.Double, item.Value.String);
            writer.WriteString("traceId", item.TraceId);
            writer.WriteString("spanId", item.SpanId);
            writer.WriteString("parentSpanId", item.ParentSpanId);
            WriteInteger(writer, "durationTicks", item.DurationTicks);
            WriteFields(writer, "fields", item.Fields);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteResult(Utf8JsonWriter writer, DiagnosticOperationResult? result)
    {
        if (result == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("status", result.Status);
        WriteFields(writer, "values", result.Values);
        if (result.Revision is long revision)
        {
            WriteInteger(writer, "revision", revision);
        }
        else
        {
            writer.WriteNull("revision");
        }

        writer.WriteString("code", result.Code);
        writer.WriteString("message", result.Message);
        writer.WriteEndObject();
    }

    private static void WriteFields(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, DiagnosticValue>? fields)
    {
        writer.WritePropertyName(name);
        if (fields == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        var values = new ValueWriter(writer);
        DiagnosticValueWriting.WriteTo(fields, ref values);
        writer.WriteEndObject();
    }

    private static void WriteInteger(Utf8JsonWriter writer, string name, long value)
    {
        Span<char> text = stackalloc char[20];
        value.TryFormat(text, out int length, provider: CultureInfo.InvariantCulture);
        writer.WriteString(name, text[..length]);
    }

    private static void WriteScalar(Utf8JsonWriter writer, DiagnosticValueKind kind, bool boolean = false, long integer = 0, double number = 0, string? text = null)
    {
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)kind);
        writer.WriteBoolean("boolean", boolean);
        WriteInteger(writer, "int64", integer);
        writer.WriteNumber("double", number);
        writer.WriteString("string", text);
        writer.WriteEndObject();
    }

    private readonly struct ValueWriter(Utf8JsonWriter writer) : IDiagnosticValueWriter
    {
        public void Write(string name, bool value)
        {
            writer.WritePropertyName(name);
            WriteScalar(writer, DiagnosticValueKind.Boolean, boolean: value);
        }

        public void Write(string name, long value)
        {
            writer.WritePropertyName(name);
            WriteScalar(writer, DiagnosticValueKind.Int64, integer: value);
        }

        public void Write(string name, double value)
        {
            writer.WritePropertyName(name);
            WriteScalar(writer, DiagnosticValueKind.Double, number: value);
        }

        public void Write(string name, string value)
        {
            writer.WritePropertyName(name);
            WriteScalar(writer, DiagnosticValueKind.String, text: value);
        }
    }
}
