namespace Lumyte.Execution;

internal sealed class GroupDefinition(OwnerDefinition owner, string name)
{
    internal OwnerDefinition Owner { get; } = owner;

    internal string Name { get; } = name;

    internal QueuedWork? Head { get; set; }

    internal QueuedWork? Tail { get; set; }
}
