namespace Lumyte.Composition;

/// <summary>Constrains generated content members on this class and its descendants to an exact collection type.</summary>
/// <param name="contentType">The required collection type.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CompositionContentTypeAttribute(Type contentType) : Attribute
{
    /// <summary>Gets the required content type; descendant declarations cannot relax ancestor constraints.</summary>
    public Type ContentType { get; } = contentType ?? throw new ArgumentNullException(nameof(contentType));
}
