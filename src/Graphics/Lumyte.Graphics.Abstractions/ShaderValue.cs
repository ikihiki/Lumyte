namespace Lumyte.Graphics.Abstractions;

/// <summary>Captures a non-owning opaque reference and its C# wire slot.</summary>
public sealed record ShaderValue(Type ValueType, object? Reference, int Offset);
