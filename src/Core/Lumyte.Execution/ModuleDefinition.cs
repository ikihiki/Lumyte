namespace Lumyte.Execution;

internal sealed class ModuleDefinition(ExecutionRuntime runtime, IExecutionModule module)
{
    internal ExecutionRuntime Runtime { get; } = runtime;

    internal string Name { get; } = module.Name;

    internal Type Type { get; } = module.GetType();

    internal List<Type> DependencyTypes { get; } = [];

    internal List<ModuleDefinition> Dependencies { get; } = [];

    internal IExecutionModule? Module { get; set; } = module;

    internal bool StartAttempted { get; set; }

    internal bool Initialized { get; set; }

    internal bool Stopped { get; set; }
}
