namespace Lumyte.Graphics.Abstractions;

/// <summary>Overrides the logical Slang type name of a shader data structure.</summary>
/// <param name="name">The logical shader name.</param>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ShaderTypeNameAttribute(string name) : Attribute
{
    /// <summary>Gets the logical shader name.</summary>
    public string Name { get; } = name;
}
