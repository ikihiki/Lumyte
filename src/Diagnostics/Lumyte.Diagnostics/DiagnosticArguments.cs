namespace Lumyte.Diagnostics;

/// <summary>Validated arguments consumed by generated handlers.</summary>
public sealed class DiagnosticArguments
{
    private readonly IReadOnlyDictionary<string, DiagnosticValue> _values;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticArguments"/> class.</summary>
    /// <param name="values">The values argument.</param>
    public DiagnosticArguments(IReadOnlyDictionary<string, DiagnosticValue> values)
    {
        _values = values;
    }

    /// <summary>Reads a boolean.</summary>
    /// <param name="id">The id argument.</param>
    /// <returns>The computed result.</returns>
    public bool GetBoolean(string id) => Get(id, DiagnosticValueKind.Boolean).Boolean;

    /// <summary>Reads an integer.</summary>
    /// <param name="id">The id argument.</param>
    /// <returns>The computed result.</returns>
    public long GetInt64(string id) => Get(id, DiagnosticValueKind.Int64).Int64;

    /// <summary>Reads a number.</summary>
    /// <param name="id">The id argument.</param>
    /// <returns>The computed result.</returns>
    public double GetDouble(string id) => Get(id, DiagnosticValueKind.Double).Double;

    /// <summary>Reads a string.</summary>
    /// <param name="id">The id argument.</param>
    /// <returns>The computed result.</returns>
    public string GetString(string id) => Get(id, DiagnosticValueKind.String).String!;

    private DiagnosticValue Get(string id, DiagnosticValueKind kind)
    {
        DiagnosticValue value = _values[id];
        return value.Kind == kind ? value : throw new ArgumentException("Scalar kind mismatch.", nameof(id));
    }
}
