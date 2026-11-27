namespace Lumyte.Pipelines;

/// <summary>Receives typed children from an explicitly declared bundle shape.</summary>
public interface IPipelineBundleVisitor
{
    /// <summary>Visits one immediate child, including its closed generic type when the value is default.</summary>
    /// <typeparam name="T">The child payload type.</typeparam>
    /// <param name="value">The immutable child.</param>
    void Child<T>(T value);
}
