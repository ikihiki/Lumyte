using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Lumyte.Diagnostics.Transport;

/// <summary>Writes game publications directly without per-event or per-field transport DTOs.</summary>
public static class DiagnosticJsonMessageEncoder
{
    private static readonly JsonEncodedText _messageIdProperty = JsonEncodedText.Encode("messageId");
    private static readonly JsonEncodedText _sessionIdProperty = JsonEncodedText.Encode("sessionId");
    private static readonly JsonEncodedText _kindProperty = JsonEncodedText.Encode("kind");
    private static readonly JsonEncodedText _requestIdProperty = JsonEncodedText.Encode("requestId");
    private static readonly JsonEncodedText _resultProperty = JsonEncodedText.Encode("result");
    private static readonly JsonEncodedText _eventsProperty = JsonEncodedText.Encode("events");
    private static readonly JsonEncodedText _timestampProperty = JsonEncodedText.Encode("timestamp");
    private static readonly JsonEncodedText _nameProperty = JsonEncodedText.Encode("name");
    private static readonly JsonEncodedText _valueProperty = JsonEncodedText.Encode("value");
    private static readonly JsonEncodedText _traceIdProperty = JsonEncodedText.Encode("traceId");
    private static readonly JsonEncodedText _spanIdProperty = JsonEncodedText.Encode("spanId");
    private static readonly JsonEncodedText _parentSpanIdProperty = JsonEncodedText.Encode("parentSpanId");
    private static readonly JsonEncodedText _durationTicksProperty = JsonEncodedText.Encode("durationTicks");
    private static readonly JsonEncodedText _fieldsProperty = JsonEncodedText.Encode("fields");
    private static readonly JsonEncodedText _statusProperty = JsonEncodedText.Encode("status");
    private static readonly JsonEncodedText _valuesProperty = JsonEncodedText.Encode("values");
    private static readonly JsonEncodedText _revisionProperty = JsonEncodedText.Encode("revision");
    private static readonly JsonEncodedText _codeProperty = JsonEncodedText.Encode("code");
    private static readonly JsonEncodedText _messageProperty = JsonEncodedText.Encode("message");
    private static readonly JsonEncodedText _booleanProperty = JsonEncodedText.Encode("boolean");
    private static readonly JsonEncodedText _int64Property = JsonEncodedText.Encode("int64");
    private static readonly JsonEncodedText _doubleProperty = JsonEncodedText.Encode("double");
    private static readonly JsonEncodedText _stringProperty = JsonEncodedText.Encode("string");

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
        writer.WriteString(_messageIdProperty, message.MessageId);
        writer.WriteString(_sessionIdProperty, message.SessionId);
        writer.WriteNumber(_kindProperty, (int)message.Kind);
        writer.WritePropertyName(_requestIdProperty);
        if (message.RequestId is Guid requestId)
        {
            writer.WriteStringValue(requestId);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WritePropertyName(_resultProperty);
        WriteResult(writer, message.Result);
        writer.WriteStartArray(_eventsProperty);
        foreach (DiagnosticEvent item in message.Events)
        {
            writer.WriteStartObject();
            writer.WriteString(_kindProperty, item.Kind);
            WriteInteger(writer, _timestampProperty, item.Timestamp);
            writer.WriteString(_nameProperty, item.Name);
            writer.WritePropertyName(_valueProperty);
            WriteScalar(writer, item.Value.Kind, item.Value.Boolean, item.Value.Int64, item.Value.Double, item.Value.String);
            writer.WriteString(_traceIdProperty, item.TraceId);
            writer.WriteString(_spanIdProperty, item.SpanId);
            writer.WriteString(_parentSpanIdProperty, item.ParentSpanId);
            WriteInteger(writer, _durationTicksProperty, item.DurationTicks);
            WriteFields(writer, _fieldsProperty, item.Fields);
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
        writer.WriteString(_statusProperty, result.Status);
        WriteFields(writer, _valuesProperty, result.Values);
        if (result.Revision is long revision)
        {
            WriteInteger(writer, _revisionProperty, revision);
        }
        else
        {
            writer.WriteNull(_revisionProperty);
        }

        writer.WriteString(_codeProperty, result.Code);
        writer.WriteString(_messageProperty, result.Message);
        writer.WriteEndObject();
    }

    private static void WriteFields(Utf8JsonWriter writer, JsonEncodedText name, IReadOnlyDictionary<string, DiagnosticValue>? fields)
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

    private static void WriteInteger(Utf8JsonWriter writer, JsonEncodedText name, long value)
    {
        Span<char> text = stackalloc char[20];
        value.TryFormat(text, out int length, provider: CultureInfo.InvariantCulture);
        writer.WriteString(name, text[..length]);
    }

    private static void WriteScalar(Utf8JsonWriter writer, DiagnosticValueKind kind, bool boolean = false, long integer = 0, double number = 0, string? text = null)
    {
        writer.WriteStartObject();
        writer.WriteNumber(_kindProperty, (int)kind);
        writer.WriteBoolean(_booleanProperty, boolean);
        WriteInteger(writer, _int64Property, integer);
        writer.WriteNumber(_doubleProperty, number);
        writer.WriteString(_stringProperty, text);
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
