namespace Lumyte.Execution;

internal sealed class GateDefinition(ModuleDefinition module, string name)
{
    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal bool Open { get; set; }

    internal Exception? Failure { get; set; }
}
