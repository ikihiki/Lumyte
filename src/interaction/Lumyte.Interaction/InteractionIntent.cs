namespace Lumyte.Interaction;

public abstract class InteractionIntent
{
    private protected InteractionIntent(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }

    public override string ToString() => Id;
}
