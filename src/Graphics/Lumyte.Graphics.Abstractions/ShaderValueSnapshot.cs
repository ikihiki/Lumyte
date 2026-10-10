namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds generated member values without owning registrations or native resources.</summary>
/// <param name="RootParameter">The logical root parameter name.</param>
/// <param name="ShaderTypeName">The logical shader type name.</param>
/// <param name="Values">The immutable member list.</param>
public sealed record ShaderValueSnapshot(string RootParameter, string ShaderTypeName, IReadOnlyList<ShaderValue> Values);
