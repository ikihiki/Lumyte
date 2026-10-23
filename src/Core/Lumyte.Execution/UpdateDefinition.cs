namespace Lumyte.Execution;

internal abstract class UpdateDefinition(ModuleDefinition module, string name, OwnerDefinition owner, GateDefinition[] gates)
{
    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal OwnerDefinition Owner { get; } = owner;

    internal GateDefinition[] Gates { get; } = gates;

    internal PhaseDefinition? Phase { get; set; }

    internal ThreadLoopOptions? Options { get; set; }

    internal long Next { get; set; }

    internal long Previous { get; set; }

    internal long Index { get; set; }

    internal abstract void Invoke(in UpdateTick tick, in WorkContext context);

    internal abstract void Clear();
}
