namespace Lumyte.Composition;

/// <summary>Receives generated content without replacing the component's collection instance.</summary>
/// <typeparam name="T">The accepted content element type.</typeparam>
public interface ICompositionContent<T>
{
    /// <summary>Initializes content according to the receiving collection's ownership and lifetime rules.</summary>
    /// <param name="content">The ordered elements; implementations must copy retained input.</param>
    void Initialize(T[] content);
}
