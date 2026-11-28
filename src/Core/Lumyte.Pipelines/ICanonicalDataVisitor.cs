namespace Lumyte.Pipelines;

/// <summary>Visits canonical unmanaged storage without reflection or type registries.</summary>
/// <typeparam name="TResult">The operation result.</typeparam>
internal interface ICanonicalDataVisitor<TResult>
{
    /// <summary>Operates on the exact canonical element type.</summary>
    /// <typeparam name="TElement">The canonical unmanaged element.</typeparam>
    /// <param name="elements">The borrowed immutable elements.</param>
    /// <returns>The visitor result.</returns>
    TResult Visit<TElement>(ReadOnlySpan<TElement> elements)
        where TElement : unmanaged;
}
