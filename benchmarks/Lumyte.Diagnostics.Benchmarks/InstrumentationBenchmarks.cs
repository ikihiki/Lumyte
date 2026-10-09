using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Diagnostics.Benchmarks;

/// <summary>Measures standard metric generation with and without diagnostic collection.</summary>
[MemoryDiagnoser]
public class InstrumentationBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private DiagnosticTelemetry _collector = null!;
    private Counter<long> _counter = null!;
    private KeyValuePair<string, object?> _tag;

    /// <summary>Gets or sets a value indicating whether collection is enabled.</summary>
    [Params(false, true)]
    public bool Enabled { get; set; }

    /// <summary>Creates and subscribes the standard meter outside measured code.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options =>
        {
            options.Enabled = Enabled;
            options.AllowedMeterNames = ["Lumyte.Benchmark"];
        });
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _collector = _scope.ServiceProvider.GetRequiredService<DiagnosticTelemetry>();
        _collector.Start();
        Meter meter = _provider.GetRequiredService<IMeterFactory>().Create(new MeterOptions("Lumyte.Benchmark"));
        _counter = meter.CreateCounter<long>("samples");
        _tag = new("lumyte.instance.id", _scope.ServiceProvider.GetRequiredService<IGameExecutionIdentity>().InstanceId.ToString("D"));
    }

    /// <summary>Records a scalar and drains the event, preventing overflow from changing the measured path.</summary>
    [Benchmark]
    public void RecordAndDrain()
    {
        _counter.Add(1, _tag);
        _collector.TryRead(out _);
    }

    /// <summary>Disposes listeners and DI services.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
