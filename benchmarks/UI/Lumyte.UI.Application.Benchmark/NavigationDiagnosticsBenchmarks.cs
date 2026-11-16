using BenchmarkDotNet.Attributes;
using Lumyte.Diagnostics;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.Navigation.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Benchmark;

/// <summary>Generated optional navigation diagnostic schema and empty-registry observation; no transport.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Diagnostic")]
public class NavigationDiagnosticsBenchmarks
{
    private readonly Dictionary<string, DiagnosticValue> _arguments = new() { ["window-context-id"] = DiagnosticValue.From(string.Empty) };
    private readonly HashSet<DiagnosticPermission> _permissions = [DiagnosticPermission.Observe];
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private DiagnosticOperationSet _operations = null!;
    private DiagnosticOperationContext _context = null!;

    /// <summary>Resolves the real optional contributor through its public DI registration.</summary>
    [GlobalSetup]
    public void Setup()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Panel()).UseDiagnostics().UseNavigationDiagnostics();
        _provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        _scope = _provider.CreateScope();
        _operations = new(_scope.ServiceProvider.GetRequiredService<UiNavigationDiagnostics>());
        _context = new(Guid.NewGuid(), Guid.NewGuid(), new(1, 0), "benchmark", null, default);
    }

    /// <summary>Copies actual generated navigation operation metadata.</summary>
    /// <returns>The detached schema.</returns>
    [Benchmark]
    public IReadOnlyList<OperationDescriptor> UiNavigationDiagnosticsCatalog() => _operations.Catalog;

    /// <summary>Observes the actual empty registry, explicitly not a configured navigation application.</summary>
    /// <returns>The generated bounded metadata result.</returns>
    [Benchmark]
    public DiagnosticOperationResult UiNavigationDiagnosticsEmptyRegistry() => _operations.Invoke("state", _arguments, _context, _permissions);

    /// <summary>Releases the diagnostic scope and root provider.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
