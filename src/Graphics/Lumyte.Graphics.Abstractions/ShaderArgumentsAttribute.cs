namespace Lumyte.Graphics.Abstractions;

/// <summary>Marks a root argument structure for codec generation.</summary>
/// <param name="rootParameter">The logical shader name.</param>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ShaderArgumentsAttribute(string rootParameter = "arguments") : Attribute
{
    /// <summary>Gets the logical shader name.</summary>
    public string RootParameter { get; } = rootParameter;
}
