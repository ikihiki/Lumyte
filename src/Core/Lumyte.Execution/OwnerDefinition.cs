namespace Lumyte.Execution;

internal sealed class OwnerDefinition(ModuleDefinition module, string name, OwnerRequirement requirement)
{
    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal OwnerRequirement Requirement { get; } = requirement;

    internal List<GroupDefinition> Groups { get; } = [];

    internal PhaseDefinition? Phase { get; set; }

    internal ThreadDefinition? Thread { get; set; }

    internal int ThreadId { get; set; }
}
