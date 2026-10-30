namespace Lumyte.Composition;

/// <summary>Constrains generated content members on this class and its descendants to an exact collection type.</summary>
/// <typeparam name="TContent">The required collection type.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CompositionContentTypeAttribute<TContent> : Attribute
{
    /// <summary>Gets the required content type; descendant declarations cannot relax ancestor constraints.</summary>
    public Type ContentType => typeof(TContent);
}
