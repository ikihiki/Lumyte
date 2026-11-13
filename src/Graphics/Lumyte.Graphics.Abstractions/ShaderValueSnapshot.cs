namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds generated member values without owning registrations or native resources.</summary>
/// <param name="RootParameter">The logical root parameter name.</param>
/// <param name="ShaderTypeName">The logical shader type name.</param>
/// <param name="Values">The immutable member list.</param>
public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, IReadOnlyList<ShaderValue> Values)
{
    /// <summary>Gets the C#-declared wire stride. Shader layouts must match it without repacking.</summary>
    public int SizeInBytes { get; init; }

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
        ShaderValue[] values = Values.Select(value => value with { Data = value.Data.ToArray() }).ToArray();
        return this with { Values = Array.AsReadOnly(values) };
    }
}
