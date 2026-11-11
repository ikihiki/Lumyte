namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies a typed numeric leaf in generated persistent shader data.</summary>
/// <typeparam name="T">The owning shader data type.</typeparam>
/// <typeparam name="TValue">The numeric value type.</typeparam>
/// <param name="Path">The logical path, checked against the compiled schema on use.</param>
public readonly record struct ShaderDataValueMember<T, TValue>(string Path)
    where T : struct, IShaderData
    where TValue : unmanaged;
