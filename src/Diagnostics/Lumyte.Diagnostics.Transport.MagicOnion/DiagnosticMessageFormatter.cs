using MessagePack;
using MessagePack.Formatters;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

/// <summary>Writes publications directly and decodes the compatible version-one wire schema.</summary>
public sealed class DiagnosticMessageFormatter : IMessagePackFormatter<DiagnosticMessage?>
{
    /// <inheritdoc/>
    public void Serialize(ref MessagePackWriter writer, DiagnosticMessage? value, MessagePackSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteArrayHeader(6);
        WriteIdentifier(ref writer, value.MessageId);
        WriteIdentifier(ref writer, value.SessionId);
        writer.Write((int)value.Kind);
        if (value.RequestId is Guid requestId)
        {
            WriteIdentifier(ref writer, requestId);
        }
        else
        {
            writer.WriteNil();
        }

        WriteResult(ref writer, value.Result);
        writer.WriteArrayHeader(value.Events.Length);
        foreach (DiagnosticEvent item in value.Events)
        {
            writer.WriteArrayHeader(9);
            writer.Write(item.Kind);
            writer.Write(item.Timestamp);
            writer.Write(item.Name);
            WriteScalar(ref writer, item.Value.Kind, item.Value.Boolean, item.Value.Int64, item.Value.Double, item.Value.String);
            writer.Write(item.TraceId);
            writer.Write(item.SpanId);
            writer.Write(item.ParentSpanId);
            writer.Write(item.DurationTicks);
            WriteFields(ref writer, item.Fields);
        }
    }

    /// <inheritdoc/>
    public DiagnosticMessage Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => DiagnosticMessageReader.Read(ref reader, options);

    private static void WriteIdentifier(ref MessagePackWriter writer, Guid value)
    {
        writer.WriteStringHeader(36);
        value.TryFormat(writer.GetSpan(36), out int written, "D");
        writer.Advance(written);
    }

    private static void WriteResult(ref MessagePackWriter writer, DiagnosticOperationResult? result)
    {
        if (result == null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(5);
        writer.Write(result.Status);
        if (result.Values == null)
        {
            writer.WriteNil();
        }
        else
        {
            WriteFields(ref writer, result.Values);
        }

        if (result.Revision is long revision)
        {
            writer.Write(revision);
        }
        else
        {
            writer.WriteNil();
        }

        writer.Write(result.Code);
        writer.Write(result.Message);
    }

    private static void WriteFields(ref MessagePackWriter writer, IReadOnlyDictionary<string, DiagnosticValue> fields)
    {
        writer.WriteMapHeader(fields.Count);
        var values = new ValueWriter(writer);
        DiagnosticValueWriting.WriteTo(fields, ref values);
        writer = values.TakeWriter();
    }

    private static void WriteScalar(ref MessagePackWriter writer, DiagnosticValueKind kind, bool boolean = false, long integer = 0, double number = 0, string? text = null)
    {
        writer.WriteArrayHeader(5);
        writer.Write((int)kind);
        writer.Write(boolean);
        writer.Write(integer);
        writer.Write(number);
        writer.Write(text);
    }

    private ref struct ValueWriter(MessagePackWriter writer) : IDiagnosticValueWriter
    {
        private MessagePackWriter _writer = writer;

        public MessagePackWriter TakeWriter() => _writer;

        public void Write(string name, bool value)
        {
            _writer.Write(name);
            WriteScalar(ref _writer, DiagnosticValueKind.Boolean, boolean: value);
        }

        public void Write(string name, long value)
        {
            _writer.Write(name);
            WriteScalar(ref _writer, DiagnosticValueKind.Int64, integer: value);
        }

        public void Write(string name, double value)
        {
            _writer.Write(name);
            WriteScalar(ref _writer, DiagnosticValueKind.Double, number: value);
        }

        public void Write(string name, string value)
        {
            _writer.Write(name);
            WriteScalar(ref _writer, DiagnosticValueKind.String, text: value);
        }
    }
}
