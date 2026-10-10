namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes one immutable numeric payload or opaque reference from a generated codec.</summary>
/// <param name="Path">The logical member path.</param>
/// <param name="ValueType">The numeric or referenced element type.</param>
/// <param name="Data">The CPU numeric representation.</param>
/// <param name="Reference">The opaque reference, or null for a numeric member.</param>
/// <param name="IsReference">Whether the member is an opaque reference.</param>
public sealed record ShaderValue(string Path, Type ValueType, ReadOnlyMemory<byte> Data, object? Reference, bool IsReference);
