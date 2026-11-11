namespace Lumyte.Graphics.Abstractions;

/// <summary>Identifies a typed reference leaf without exposing its backend representation.</summary>
/// <typeparam name="T">The owning shader data type.</typeparam>
/// <typeparam name="TReference">The referenced logical element type.</typeparam>
/// <param name="Path">The logical path, checked against the compiled schema on use.</param>
public readonly record struct ShaderDataReferenceMember<T, TReference>(string Path)
    where T : struct, IShaderData;
