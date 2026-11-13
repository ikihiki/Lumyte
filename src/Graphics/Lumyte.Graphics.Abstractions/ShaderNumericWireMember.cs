namespace Lumyte.Graphics.Abstractions;

/// <summary>Declares one numeric member of an explicitly laid-out C# GPU wire element.</summary>
/// <param name="Path">The shader logical member corresponding to this C# field.</param>
/// <param name="ValueType">The exact supported numeric CLR type.</param>
/// <param name="Offset">The C# field offset in bytes.</param>
/// <param name="Size">The C# field storage size in bytes.</param>
public sealed record ShaderNumericWireMember(string Path, Type ValueType, int Offset, int Size);
