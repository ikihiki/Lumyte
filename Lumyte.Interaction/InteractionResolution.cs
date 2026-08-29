namespace Lumyte.Interaction;

public abstract record InteractionResolution<T>
{
    private InteractionResolution()
    {
    }

    public sealed record NoMatch : InteractionResolution<T>;

    public sealed record Match(InteractionCandidate<T> Candidate) : InteractionResolution<T>;

    public sealed record Conflict(IReadOnlyList<InteractionCandidate<T>> Candidates) : InteractionResolution<T>;
}
