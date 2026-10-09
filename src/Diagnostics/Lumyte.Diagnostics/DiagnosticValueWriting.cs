namespace Lumyte.Diagnostics;

/// <summary>Writes generated output directly and supports collected dynamic scalar fields.</summary>
public static class DiagnosticValueWriting
{
    /// <summary>Writes fields without materializing generated output.</summary>
    /// <typeparam name="TWriter">The physical or validating writer.</typeparam>
    /// <param name="values">The immutable values.</param>
    /// <param name="writer">The synchronous writer.</param>
    public static void WriteTo<TWriter>(IReadOnlyDictionary<string, DiagnosticValue> values, ref TWriter writer)
        where TWriter : IDiagnosticValueWriter, allows ref struct
    {
        if (values is DiagnosticOutputValues output)
        {
            output.WriteTo(ref writer);
            return;
        }

        foreach ((string name, DiagnosticValue value) in values)
        {
            switch (value.Kind)
            {
                case DiagnosticValueKind.Boolean:
                    writer.Write(name, value.Boolean);
                    break;
                case DiagnosticValueKind.Int64:
                    writer.Write(name, value.Int64);
                    break;
                case DiagnosticValueKind.Double:
                    writer.Write(name, value.Double);
                    break;
                case DiagnosticValueKind.String:
                    writer.Write(name, value.String!);
                    break;
                default:
                    throw new ArgumentException("Unknown diagnostic scalar kind.", nameof(values));
            }
        }
    }
}
