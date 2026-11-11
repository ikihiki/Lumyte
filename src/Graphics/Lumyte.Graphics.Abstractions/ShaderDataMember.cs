namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes one generated logical leaf independently of its initial value.</summary>
/// <param name="Path">The complete logical member path.</param>
/// <param name="ValueType">The numeric type or referenced logical element type.</param>
/// <param name="IsReference">Whether the member is an opaque GPU reference.</param>
/// <param name="ReferenceTarget">The generated logical buffer element schema name, when applicable.</param>
public sealed record ShaderDataMember(string Path, Type ValueType, bool IsReference, string? ReferenceTarget = null);
