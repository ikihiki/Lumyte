namespace Lumyte.Interaction;

public sealed class InteractionResolver
{
    public InteractionResolution<T> Resolve<T>(
        IEnumerable<InteractionCandidate<T>> candidates,
        InteractionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);
        InteractionCandidate<T>[] eligible =
        [
            .. candidates
                .Where(candidate => candidate.When.Evaluate(context))
                .OrderByDescending(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Specificity)
                .ThenByDescending(candidate => candidate.SourcePriority),
        ];
        if (eligible.Length == 0)
        {
            return new InteractionResolution<T>.NoMatch();
        }

        InteractionCandidate<T> best = eligible[0];
        InteractionCandidate<T>[] tied =
        [
            .. eligible.TakeWhile(candidate =>
                candidate.Priority == best.Priority
                && candidate.Specificity == best.Specificity
                && candidate.SourcePriority == best.SourcePriority),
        ];
        return tied.Length == 1
            ? new InteractionResolution<T>.Match(best)
            : new InteractionResolution<T>.Conflict(tied);
    }
}
