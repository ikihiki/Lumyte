namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds generated member values without owning registrations or native resources.</summary>
/// <param name="RootParameter">The logical root parameter name.</param>
/// <param name="ShaderTypeName">The logical shader type name.</param>
/// <param name="Values">The immutable member list.</param>
public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, IReadOnlyList<ShaderValue> Values)
{
    /// <summary>Copies the member list and numeric bytes without taking ownership of referenced GPU resources.</summary>
    /// <returns>An independent snapshot retaining the same opaque resource references.</returns>
    public ShaderValueSnapshot Copy()
    {
        ArgumentNullException.ThrowIfNull(Values);
        ShaderValue[] values = Values.Select(value => value with { Data = value.Data.ToArray() }).ToArray();
        return new(RootParameter, ShaderTypeName, Array.AsReadOnly(values));
    }
}
