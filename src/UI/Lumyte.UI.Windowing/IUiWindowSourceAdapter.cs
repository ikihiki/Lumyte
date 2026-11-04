namespace Lumyte.UI.Windowing;

/// <summary>Preserves drawing identity projection when a backend creates another physical source.</summary>
public interface IUiWindowSourceAdapter : IUiWindowSource
{
    /// <summary>Gets the wrapped source for backend-specific capability resolution.</summary>
    IUiWindowSource InnerSource { get; }

    /// <summary>Applies this adapter chain to a newly created physical source.</summary>
    /// <param name="source">The new borrowed physical source.</param>
    /// <returns>A source preserving the same public identity projection.</returns>
    IUiWindowSource AdaptSource(IUiWindowSource source);
}
