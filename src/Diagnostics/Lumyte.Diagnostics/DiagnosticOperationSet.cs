namespace Lumyte.Diagnostics;

/// <summary>An immutable generated catalog with validated direct dispatch.</summary>
public sealed class DiagnosticOperationSet
{
    private readonly IReadOnlyDictionary<string, (OperationDescriptor Descriptor, DiagnosticOperationHandler Handler)> _operations;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticOperationSet"/> class.</summary>
    /// <param name="contributor">The contributor argument.</param>
    public DiagnosticOperationSet(IDiagnosticContributor contributor)
    {
        var builder = new DiagnosticBuilder();
        contributor.Configure(builder);
        builder.Seal();
        _operations = builder.Operations;
    }

    /// <summary>Gets a detached catalog.</summary>
    public IReadOnlyList<OperationDescriptor> Catalog => _operations.Values.Select(item => item.Descriptor with
    {
        Arguments = [.. item.Descriptor.Arguments],
        Results = [.. item.Descriptor.Results],
    }).ToArray();

    /// <summary>Invokes a validated operation on the caller's owning thread.</summary>
    /// <param name="id">The id argument.</param>
    /// <param name="values">The values argument.</param>
    /// <param name="context">The context argument.</param>
    /// <param name="permissions">The permissions argument.</param>
    /// <returns>The computed result.</returns>
    public DiagnosticOperationResult Invoke(string id, IReadOnlyDictionary<string, DiagnosticValue> values, DiagnosticOperationContext context, IReadOnlySet<DiagnosticPermission> permissions)
    {
        if (!_operations.TryGetValue(id, out (OperationDescriptor Descriptor, DiagnosticOperationHandler Handler) operation))
        {
            return DiagnosticOperationResult.Reject("not-found", "Unknown operation.");
        }

        if (!permissions.Contains(operation.Descriptor.RequiredPermission))
        {
            return DiagnosticOperationResult.Reject("forbidden", "Operation is not permitted.");
        }

        if (context.CancellationToken.IsCancellationRequested)
        {
            return DiagnosticOperationResult.Reject("cancelled", "Operation was cancelled before execution.");
        }

        if (operation.Descriptor.RequiresRevision && context.ExpectedRevision is null)
        {
            return DiagnosticOperationResult.Reject("revision-required", "Expected revision is required.");
        }

        if (!Validate(operation.Descriptor.Arguments, values))
        {
            return DiagnosticOperationResult.Reject("invalid-arguments", "Arguments do not match the schema.");
        }

        try
        {
            DiagnosticOperationResult result = operation.Handler(context, new(values));
            return result.Status != "success" || (result.Values != null && Validate(operation.Descriptor.Results, result.Values))
                ? result : DiagnosticOperationResult.Reject("invalid-result", "Operation returned invalid output.");
        }
        catch (Exception)
        {
            return DiagnosticOperationResult.Reject("execution-failed", "Operation failed.");
        }
    }

    private static bool Validate(DiagnosticField[] fields, IReadOnlyDictionary<string, DiagnosticValue> values)
    {
        if (fields.Length != values.Count)
        {
            return false;
        }

        foreach (DiagnosticField field in fields)
        {
            if (!values.TryGetValue(field.Id, out DiagnosticValue value) || value.Kind != field.Kind)
            {
                return false;
            }

            if (value.Kind == DiagnosticValueKind.String && (value.String == null || value.String.Length > (field.MaxLength ?? 4096)))
            {
                return false;
            }

            if (value.Kind == DiagnosticValueKind.Double && (!double.IsFinite(value.Double) || value.Double < field.Minimum || value.Double > field.Maximum))
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
}
