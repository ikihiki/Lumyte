using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Execution.Benchmark;

/// <summary>Measures actual DI module collection and runtime plan construction.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Core", "Execution", "DependencyInjection", "Screening", "Comparison", "Diagnostic")]
public class DependencyInjectionBenchmarks
{
    /// <summary>Resolves modules through the public runtime constructor and prepares the plan.</summary>
    /// <returns>The number of modules supplied by the service provider.</returns>
    [Benchmark]
    [BenchmarkCategory("Configuration")]
    public int ResolveAndPrepare()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutionModule>(_ => new WorkModule(false));
        services.AddSingleton<ExecutionRuntime>();
        using ServiceProvider provider = services.BuildServiceProvider();
        ExecutionRuntime runtime = provider.GetRequiredService<ExecutionRuntime>();
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        return provider.GetServices<IExecutionModule>().Count();
    }
}
