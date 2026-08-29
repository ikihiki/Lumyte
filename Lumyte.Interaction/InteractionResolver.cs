namespace Lumyte.Interaction;

public sealed class InteractionResolver
{
    public InteractionResolution<T> Resolve<T>(
        IEnumerable<InteractionCandidate<T>> candidates,
        InteractionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);
        InteractionCandidate<T>? best = null;
        List<InteractionCandidate<T>>? tied = null;
        foreach (InteractionCandidate<T> candidate in candidates)
        {
            if (!candidate.When.Evaluate(context))
            {
                continue;
            }

            int comparison = best is null ? 1 : Compare(candidate, best);
            if (comparison > 0)
            {
                best = candidate;
                tied = null;
            }
            else if (comparison == 0)
            {
                tied ??= [best!];
                tied.Add(candidate);
            }
        }

        if (best is null)
        {
            return new InteractionResolution<T>.NoMatch();
        }

        return tied is null
            ? new InteractionResolution<T>.Match(best)
            : new InteractionResolution<T>.Conflict(tied);
    }

    private static int Compare<T>(InteractionCandidate<T> left, InteractionCandidate<T> right)
    {
        int priority = left.Priority.CompareTo(right.Priority);
        if (priority != 0)
        {
            return priority;
        }

        int specificity = left.Specificity.CompareTo(right.Specificity);
        return specificity != 0
            ? specificity
            : left.SourcePriority.CompareTo(right.SourcePriority);
    }
}
