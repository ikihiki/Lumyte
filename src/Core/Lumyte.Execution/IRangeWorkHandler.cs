namespace Lumyte.Execution;

/// <summary>A finite range handler.</summary>
/// <typeparam name="TContext">The shared graph input type.</typeparam>
public interface IRangeWorkHandler<TContext>
    where TContext : struct
{
    /// <summary>Evaluates the range length once before admission.</summary>
    /// <param name="context">The input.</param>
    /// <returns>A nonnegative length.</returns>
    int GetLength(in TContext context);

    /// <summary>Processes a disjoint range.</summary>
    /// <param name="context">The input.</param>
    /// <param name="first">The inclusive start.</param>
    /// <param name="endExclusive">The exclusive end.</param>
    void Execute(in TContext context, int first, int endExclusive);
}
