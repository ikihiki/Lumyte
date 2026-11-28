namespace Lumyte.Pipelines;

/// <summary>Preserves the canonical element type for trim-safe platform operations.</summary>
internal interface ICanonicalStorage
{
    /// <summary>Invokes a typed visitor without exposing mutable storage.</summary>
    /// <typeparam name="TResult">The operation result.</typeparam>
    /// <param name="visitor">The explicit operation.</param>
    /// <returns>The value produced by the typed visitor.</returns>
    TResult Accept<TResult>(ICanonicalDataVisitor<TResult> visitor);
}
