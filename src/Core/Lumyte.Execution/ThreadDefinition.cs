namespace Lumyte.Execution;

internal sealed class ThreadDefinition(ModuleDefinition module, string name)
{
    private int _cursor;

    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal List<OwnerDefinition> Owners { get; } = [];

    internal List<GroupDefinition> Groups { get; } = [];

    internal List<UpdateDefinition> Updates { get; } = [];

    internal ThreadLoopOptions Options { get; set; } = new(TimeSpan.FromMilliseconds(10), 64, TimeSpan.FromMilliseconds(2));

    internal AutoResetEvent? Wake { get; set; }

    internal Thread? PhysicalThread { get; set; }

    internal bool Exit { get; set; }

    internal ref int Cursor => ref _cursor;
}
