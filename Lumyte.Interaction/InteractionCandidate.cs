namespace Lumyte.Interaction;

public sealed record InteractionCandidate<T>(T Value)
{
    public ContextCondition When { get; init; } = ContextCondition.Always;

    public int Priority { get; init; }

    public int Specificity { get; init; }

    public int SourcePriority { get; init; }
}
