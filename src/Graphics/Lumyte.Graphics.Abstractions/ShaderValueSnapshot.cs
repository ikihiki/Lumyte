namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds raw C# bytes and non-owning resource reference identities.</summary>
/// <param name="RootParameter">The logical root parameter name.</param>
/// <param name="ShaderTypeName">The logical shader type name.</param>
/// <param name="Data">The C# wire bytes.</param>
/// <param name="Values">The logical reference identities and diagnostic slot positions.</param>
/// <param name="Fields">Explicitly supplied C# diagnostic metadata; captured values normally defer this probe.</param>
public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, ReadOnlyMemory<byte> Data, IReadOnlyList<ShaderValue> Values, IReadOnlyList<ShaderTransferField> Fields)
{
    /// <summary>Gets a deferred CPU diagnostic provider or explicit override. Argument captures use a static generic provider, never an instance closure.</summary>
    public Func<IReadOnlyList<ShaderTransferField>>? DiagnosticFields { get; init; }

    /// <summary>Gets the value type inspected only when diagnostics are explicitly requested.</summary>
    public Type? DiagnosticType { get; init; }

    /// <summary>Explicitly probes diagnostic metadata in any build configuration.</summary>
    /// <returns>The deferred C# layout, or the metadata supplied with this snapshot.</returns>
    public IReadOnlyList<ShaderTransferField> GetDiagnosticFields() => DiagnosticFields?.Invoke()
        ?? (DiagnosticType is { } type ? ShaderTransfer.Fields(type) : Fields);

    /// <summary>Copies the member list and numeric bytes without taking ownership of referenced GPU resources.</summary>
    /// <returns>An independent snapshot retaining the same opaque resource references.</returns>
    public ShaderValueSnapshot Copy()
    {
        ArgumentNullException.ThrowIfNull(Values);
        ShaderValue[] values = Values.ToArray();
        return this with { Data = Data.ToArray(), Values = Array.AsReadOnly(values), Fields = Array.AsReadOnly(Fields.ToArray()) };
    }
}
