using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

internal static class DiagnosticProtocol
{
    public const int MaximumCommandStringLength = 1024 * 1024;

    public static void Validate(ClientHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        Require(hello.ProtocolVersion is 1 or 2 && hello.InstanceId != Guid.Empty && hello.Catalog is { Length: > 0 and <= 32 });
        var subsystems = new HashSet<string>(StringComparer.Ordinal);
        foreach (DiagnosticSubsystemCatalog catalog in hello.Catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(catalog.Subsystem);
            Require(Text(catalog.Subsystem.Id, 128) && Text(catalog.Subsystem.DisplayName, 256) && catalog.Subsystem.SchemaVersion > 0 && subsystems.Add(catalog.Subsystem.Id)
                && catalog.Operations is { Count: <= 64 });
            var builder = new DiagnosticBuilder();
            foreach (OperationDescriptor operation in catalog.Operations)
            {
                ArgumentNullException.ThrowIfNull(operation);
                Require(Text(operation.Id, 128) && Text(operation.DisplayName, 256) && operation.Arguments is { Length: <= 32 } && operation.Results is { Length: <= 32 }
                    && Enum.IsDefined(operation.Traffic) && (hello.ProtocolVersion == 2 || operation.Traffic == DiagnosticOperationTraffic.Standard));
                builder.Operation(operation, (_, _) => DiagnosticOperationResult.Reject("unused", "Unused"));
                foreach (DiagnosticField field in operation.Arguments.Concat(operation.Results))
                {
                    Require(Text(field.Id, 128) && field.MaxLength is not > MaximumCommandStringLength
                        && (hello.ProtocolVersion == 2 || field.MaxLength is not > 4096));
                }
            }
        }
    }

    public static void Validate(OperationInvocation invocation)
    {
        Require(invocation.RequestId != Guid.Empty && Text(invocation.SubsystemId, 128) && Text(invocation.OperationId, 128)
            && invocation.TimeoutMilliseconds is >= 1 and <= 30000 && Fields(invocation.Arguments, stringMaximum: MaximumCommandStringLength));
    }

    public static void Validate(DiagnosticMessage message)
    {
        Require(message.MessageId != Guid.Empty && message.Events is { Length: <= 128 } && Enum.IsDefined(message.Kind));
        if (message.Kind == DiagnosticMessageKind.CommandResult)
        {
            Require(message.RequestId != null && message.Result != null && message.Events.Length == 0);
            Require(message.Result!.Status is "success" or "rejected" or "conflict"
                && (message.Result.Values == null || Fields(message.Result.Values, stringMaximum: MaximumCommandStringLength))
                && (message.Result.Code == null || Text(message.Result.Code, 128))
                && (message.Result.Message == null || message.Result.Message.Length <= 4096));
        }
        else
        {
            Require(message.Result == null && message.RequestId == null && (message.Kind != DiagnosticMessageKind.Heartbeat || message.Events.Length == 0));
            foreach (DiagnosticEvent item in message.Events)
            {
                ArgumentNullException.ThrowIfNull(item);
                Require(item.Kind is "metric" or "span" or "log" && Text(item.Name, 256) && Scalar(item.Value) && Fields(item.Fields, 36)
                    && (item.TraceId == null || item.TraceId.Length <= 64) && (item.SpanId == null || item.SpanId.Length <= 32)
                    && (item.ParentSpanId == null || item.ParentSpanId.Length <= 32));
            }
        }
    }

    public static void ValidateArguments(OperationInvocation invocation, OperationDescriptor operation)
        => Require(SchemaFields(invocation.Arguments, operation.Arguments));

    public static void ValidateResult(DiagnosticOperationResult result, OperationDescriptor operation)
        => Require(result.Status == "success"
            ? SchemaFields(result.Values, operation.Results)
            : result.Values == null || result.Values.Count == 0);

    private static bool Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    private static bool Fields(IReadOnlyDictionary<string, DiagnosticValue>? values, int maximum = 32, int stringMaximum = 4096)
    {
        if (values == null || values.Count > maximum)
        {
            return false;
        }

        foreach (KeyValuePair<string, DiagnosticValue> pair in values)
        {
            if (!Text(pair.Key, 128) || !Scalar(pair.Value, stringMaximum))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SchemaFields(IReadOnlyDictionary<string, DiagnosticValue>? values, DiagnosticField[] fields)
    {
        if (values == null || values.Count != fields.Length)
        {
            return false;
        }

        foreach (DiagnosticField field in fields)
        {
            if (!values.TryGetValue(field.Id, out DiagnosticValue value) || value.Kind != field.Kind
                || !Scalar(value, field.MaxLength ?? 4096))
            {
                return false;
            }

            if (value.Kind == DiagnosticValueKind.Double && (value.Double < field.Minimum || value.Double > field.Maximum))
            {
                return false;
            }

            if (value.Kind == DiagnosticValueKind.Int64 && ((field.Minimum is double min && (decimal)value.Int64 < (decimal)min)
                || (field.Maximum is double max && (decimal)value.Int64 > (decimal)max)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Scalar(DiagnosticValue value, int stringMaximum = 4096) => value.Kind switch
    {
        DiagnosticValueKind.Boolean => value.Int64 == 0 && value.Double == 0 && value.String == null,
        DiagnosticValueKind.Int64 => !value.Boolean && value.Double == 0 && value.String == null,
        DiagnosticValueKind.Double => !value.Boolean && value.Int64 == 0 && value.String == null && double.IsFinite(value.Double),
        DiagnosticValueKind.String => !value.Boolean && value.Int64 == 0 && value.Double == 0 && value.String != null && value.String.Length <= stringMaximum,
        _ => false,
    };

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
        {
            throw new ArgumentException("Invalid diagnostic protocol data.");
        }
    }
}
