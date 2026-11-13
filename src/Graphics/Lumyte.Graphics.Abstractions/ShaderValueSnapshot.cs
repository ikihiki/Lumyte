namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds raw C# bytes and non-owning resource reference identities.</summary>
/// <param name="RootParameter">The logical root parameter name.</param>
/// <param name="ShaderTypeName">The logical shader type name.</param>
/// <param name="Data">The C# wire bytes.</param>
/// <param name="Values">The immutable reference fixups.</param>
/// <param name="Fields">The C# wire layout.</param>
public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, ReadOnlyMemory<byte> Data, IReadOnlyList<ShaderValue> Values, IReadOnlyList<ShaderTransferField> Fields)
{
    /// <summary>Gets a deferred field probe, evaluated only by explicitly enabled diagnostics.</summary>
    public Func<IReadOnlyList<ShaderTransferField>>? DiagnosticFields { get; init; }

    internal Action<IReadOnlyCollection<IShaderReference>>? ExecutionReferenceValidation { get; init; }

    /// <summary>Applies host constraints to a reference at native command-local capture time.</summary>
    /// <param name="references">The complete root and transitive reference set captured for this command.</param>
    /// <remarks>Ordinary snapshots have no extra host constraint. Packing callbacks do not call this method.</remarks>
    public void ValidateExecutionReferences(IReadOnlyCollection<IShaderReference> references) => ExecutionReferenceValidation?.Invoke(references);

    /// <summary>Copies the member list and numeric bytes without taking ownership of referenced GPU resources.</summary>
    /// <returns>An independent snapshot retaining the same opaque resource references.</returns>
    public ShaderValueSnapshot Copy()
    {
        ArgumentNullException.ThrowIfNull(Values);
        ShaderValue[] values = Values.ToArray();
        return this with { Data = Data.ToArray(), Values = Array.AsReadOnly(values), Fields = Array.AsReadOnly(Fields.ToArray()) };
    }
}
