namespace Lumyte.Diagnostics;

/// <summary>Collects one atomic operation catalog.</summary>
public sealed class DiagnosticBuilder
{
    private readonly Dictionary<string, (OperationDescriptor Descriptor, DiagnosticOperationHandler Handler)> _operations = new(StringComparer.Ordinal);
    private bool _sealed;

    internal IReadOnlyDictionary<string, (OperationDescriptor Descriptor, DiagnosticOperationHandler Handler)> Operations => _operations;

    /// <summary>Registers an operation.</summary>
    /// <param name="descriptor">The descriptor argument.</param>
    /// <param name="handler">The handler argument.</param>
    public void Operation(OperationDescriptor descriptor, DiagnosticOperationHandler handler)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("The builder has been sealed.");
        }

        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(descriptor.Id) || !Enum.IsDefined(descriptor.RequiredPermission))
        {
            throw new ArgumentException("Invalid operation descriptor.", nameof(descriptor));
        }

        ValidateFields(descriptor.Arguments);
        ValidateFields(descriptor.Results);
        _operations.Add(descriptor.Id, (descriptor with { Arguments = [.. descriptor.Arguments], Results = [.. descriptor.Results] }, handler));
    }

    internal void Seal() => _sealed = true;

    private static void ValidateFields(DiagnosticField[] fields)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DiagnosticField field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Id) || !ids.Add(field.Id) || !Enum.IsDefined(field.Kind)
                || field.MaxLength is < 0 || field.Minimum > field.Maximum
                || (field.Minimum is double min && !double.IsFinite(min))
                || (field.Maximum is double max && !double.IsFinite(max))
                || ((field.Minimum != null || field.Maximum != null) && field.Kind is not DiagnosticValueKind.Int64 and not DiagnosticValueKind.Double)
                || (field.MaxLength != null && field.Kind != DiagnosticValueKind.String)
                || (field.Kind == DiagnosticValueKind.Int64 && ((field.Minimum is double lower && (Math.Abs(lower) > 9007199254740991 || lower != Math.Truncate(lower)))
                    || (field.Maximum is double upper && (Math.Abs(upper) > 9007199254740991 || upper != Math.Truncate(upper))))))
            {
                throw new ArgumentException("Invalid field schema.", nameof(fields));
            }
        }
    }
}
