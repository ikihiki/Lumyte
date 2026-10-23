namespace Lumyte.Execution;

internal sealed class PhaseDefinition(ModuleDefinition module, string name)
{
    private int _cursor;

    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal List<GroupDefinition> Groups { get; } = [];

    internal List<UpdateDefinition> Updates { get; } = [];

    internal ref int Cursor => ref _cursor;

    internal int UpdateCursor { get; set; }

    internal bool InPass { get; set; }

    internal long Limit { get; set; }
}
