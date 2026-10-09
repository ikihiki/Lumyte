using MessagePack;

namespace Lumyte.Diagnostics.Transport.MagicOnion;

internal static class DiagnosticMessageReader
{
    public static DiagnosticMessage Read(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadArrayHeader();
            if (count < 3)
            {
                throw new MessagePackSerializationException("A publication requires its identifiers and kind.");
            }

            Guid messageId = ReadIdentifier(ref reader);
            Guid sessionId = ReadIdentifier(ref reader);
            var kind = (DiagnosticMessageKind)reader.ReadInt32();
            Guid? requestId = count > 3 && !reader.TryReadNil() ? ReadIdentifier(ref reader) : null;
            DiagnosticOperationResult? result = count > 4 && !reader.TryReadNil() ? ReadResult(ref reader, options) : null;
            DiagnosticEvent[] events = count > 5 ? ReadEvents(ref reader, options) : [];
            SkipRemaining(ref reader, count - 6, options);
            return new(messageId, sessionId, kind, requestId, result, events);
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static DiagnosticOperationResult ReadResult(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadArrayHeader();
            string status = count > 0 ? ReadRequiredString(ref reader) : string.Empty;
            Dictionary<string, DiagnosticValue>? values = count > 1 && !reader.TryReadNil() ? ReadFields(ref reader, options, 32) : null;
            long? revision = count > 2 && !reader.TryReadNil() ? reader.ReadInt64() : null;
            string? code = count > 3 ? reader.ReadString() : null;
            string? message = count > 4 ? reader.ReadString() : null;
            SkipRemaining(ref reader, count - 5, options);
            return new(status, values, revision, code, message);
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static DiagnosticEvent[] ReadEvents(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadArrayHeader();
            if (count > 128)
            {
                throw new MessagePackSerializationException("A publication may contain at most 128 events.");
            }

            if (count == 0)
            {
                return [];
            }

            var events = new DiagnosticEvent[count];
            for (int index = 0; index < count; index++)
            {
                events[index] = ReadEvent(ref reader, options);
            }

            return events;
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static DiagnosticEvent ReadEvent(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadArrayHeader();
            string kind = count > 0 ? ReadRequiredString(ref reader) : string.Empty;
            long timestamp = count > 1 ? reader.ReadInt64() : 0;
            string name = count > 2 ? ReadRequiredString(ref reader) : string.Empty;
            DiagnosticValue value = count > 3 ? ReadValue(ref reader, options) : default;
            string? traceId = count > 4 ? reader.ReadString() : null;
            string? spanId = count > 5 ? reader.ReadString() : null;
            string? parentSpanId = count > 6 ? reader.ReadString() : null;
            long durationTicks = count > 7 ? reader.ReadInt64() : 0;
            Dictionary<string, DiagnosticValue> fields = count > 8 ? ReadFields(ref reader, options, 36) : new(StringComparer.Ordinal);
            SkipRemaining(ref reader, count - 9, options);
            return new(kind, timestamp, name, value, traceId, spanId, parentSpanId, durationTicks, fields);
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static Dictionary<string, DiagnosticValue> ReadFields(ref MessagePackReader reader, MessagePackSerializerOptions options, int maximum)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadMapHeader();
            if (count > maximum)
            {
                throw new MessagePackSerializationException("The diagnostic field count exceeds the protocol limit.");
            }

            var fields = new Dictionary<string, DiagnosticValue>(count, options.Security.GetEqualityComparer<string>());
            for (int index = 0; index < count; index++)
            {
                string name = ReadRequiredString(ref reader);
                if (!fields.TryAdd(name, ReadValue(ref reader, options)))
                {
                    throw new MessagePackSerializationException("Duplicate diagnostic field names are not allowed.");
                }
            }

            return fields;
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static DiagnosticValue ReadValue(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        options.Security.DepthStep(ref reader);
        try
        {
            int count = reader.ReadArrayHeader();
            DiagnosticValueKind kind = count > 0 ? (DiagnosticValueKind)reader.ReadInt32() : default;
            bool boolean = count > 1 && reader.ReadBoolean();
            long integer = count > 2 ? reader.ReadInt64() : 0;
            double number = count > 3 ? reader.ReadDouble() : 0;
            string? text = count > 4 ? reader.ReadString() : null;
            SkipRemaining(ref reader, count - 5, options);
            return new(kind, boolean, integer, number, text);
        }
        finally
        {
            reader.Depth--;
        }
    }

    private static Guid ReadIdentifier(ref MessagePackReader reader)
    {
        if (!Guid.TryParse(reader.ReadString(), out Guid value))
        {
            throw new MessagePackSerializationException("A diagnostic identifier must be a UUID string.");
        }

        return value;
    }

    private static string ReadRequiredString(ref MessagePackReader reader) => reader.ReadString()
        ?? throw new MessagePackSerializationException("A required diagnostic string cannot be nil.");

    private static void SkipRemaining(ref MessagePackReader reader, int count, MessagePackSerializerOptions options)
    {
        for (int index = 0; index < count; index++)
        {
            SkipValue(ref reader, options);
        }
    }

    private static void SkipValue(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // MessagePackReader.Skip does not enforce the configured object graph depth.
        // Unknown fields must follow the same depth bound as the known schema.
        if (reader.NextMessagePackType is MessagePackType.Array or MessagePackType.Map)
        {
            options.Security.DepthStep(ref reader);
            try
            {
                bool map = reader.NextMessagePackType == MessagePackType.Map;
                int count = map ? reader.ReadMapHeader() : reader.ReadArrayHeader();
                for (int index = 0; index < count; index++)
                {
                    SkipValue(ref reader, options);
                    if (map)
                    {
                        SkipValue(ref reader, options);
                    }
                }
            }
            finally
            {
                reader.Depth--;
            }
        }
        else
        {
            reader.Skip();
        }
    }
}
