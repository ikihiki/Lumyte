namespace Lumyte.Execution;

internal abstract class WorkDefinition(ModuleDefinition module, string name, WorkOptions options, GateDefinition[] gates)
{
    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal OwnerDefinition? Owner { get; } = options.Target.Owner;

    internal GroupDefinition? Group { get; } = options.OrderGroup.Definition;

    internal WorkAdmission Admission { get; } = options.Admission;

    internal GateDefinition[] Gates { get; } = gates;

    internal abstract long PayloadBytes { get; }

    internal abstract ICommandEndpoint Endpoint { get; }

    internal abstract void Clear();
}
