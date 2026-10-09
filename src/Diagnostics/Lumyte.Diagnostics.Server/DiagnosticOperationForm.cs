using System.Globalization;
using Lumyte.Diagnostics.Transport;

namespace Lumyte.Diagnostics.Server;

internal sealed class DiagnosticOperationForm(OperationDescriptor operation)
{
    public Dictionary<string, string> Values { get; } = operation.Arguments.ToDictionary(field => field.Id, field => field.Kind == DiagnosticValueKind.Boolean ? "true" : string.Empty, StringComparer.Ordinal);

    public string Revision { get; set; } = string.Empty;

    public OperationInvocation Build(string subsystemId)
    {
        Dictionary<string, DiagnosticValue> arguments = operation.Arguments.ToDictionary(field => field.Id, field => Parse(field, Values[field.Id]), StringComparer.Ordinal);
        long? revision = operation.RequiresRevision ? Parse(new("expected-revision", DiagnosticValueKind.Int64), Revision).Int64 : null;
        return new(Guid.NewGuid(), subsystemId, operation.Id, arguments, revision, 5000);
    }

    internal static DiagnosticValue Parse(DiagnosticField field, string raw)
    {
        DiagnosticValue value = field.Kind switch
        {
            DiagnosticValueKind.Boolean when bool.TryParse(raw, out bool boolean) => DiagnosticValue.From(boolean),
            DiagnosticValueKind.Int64 when long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer) => DiagnosticValue.From(integer),
            DiagnosticValueKind.Double when double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) => DiagnosticValue.From(number),
            DiagnosticValueKind.String when raw.Length <= (field.MaxLength ?? 4096) => DiagnosticValue.From(raw),
            _ => throw new ArgumentException(field.Id + ": 公開された型・長さで入力してください。"),
        };
        if ((value.Kind == DiagnosticValueKind.Int64 && ((field.Minimum is double lower && (decimal)value.Int64 < (decimal)lower) || (field.Maximum is double upper && (decimal)value.Int64 > (decimal)upper)))
            || (value.Kind == DiagnosticValueKind.Double && (value.Double < field.Minimum || value.Double > field.Maximum)))
        {
            throw new ArgumentException(field.Id + ": 公開された範囲内で入力してください。");
        }

        return value;
    }
}
