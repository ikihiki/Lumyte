namespace Lumyte.Graphics;

/// <summary>Identifies registered GPU resources without exposing addresses or physical bindings.</summary>
/// <typeparam name="T">The resource or logical buffer element type.</typeparam>
public interface IGpuRef<T>
{
    /// <summary>Gets the number of logical elements; texture views and samplers contain one element.</summary>
    ulong Count { get; }

    /// <summary>References one element relative to this registered range without copying data.</summary>
    /// <param name="index">The zero-based logical element index.</param>
    /// <returns>The opaque single-element reference sharing the registration's lifetime.</returns>
    IGpuRef<T> GetElement(ulong index);
}
