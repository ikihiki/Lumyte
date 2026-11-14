using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Settings.Benchmark;

/// <summary>Measures memory-backed settings transactions, separate from file integration.</summary>
public class SettingsBenchmarks
{
    private ServiceProvider _provider = null!;
    private IEditableOptions<BenchmarkModel> _settings = null!;

    /// <summary>Gets or sets the copied collection size.</summary>
    [Params(0, 16, 256)]
    public int Items { get; set; }

    /// <summary>Creates the initialized memory-backed module.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _provider = CreateProvider(new MemoryStore(), Items);
        _settings = _provider.GetRequiredService<IEditableOptions<BenchmarkModel>>();
    }

    /// <summary>Releases the provider.</summary>
    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    /// <summary>Registers and validates the complete module.</summary>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-01")]
    public void RegisterAndValidate()
    {
        using ServiceProvider provider = CreateProvider(new MemoryStore(), Items);
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
    }

    /// <summary>Deep copies the committed snapshot.</summary>
    /// <returns>The copied collection length.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-02")]
    public int Snapshot() => _settings.Current.Value.Values.Count;

    /// <summary>Creates a detached editable candidate.</summary>
    /// <returns>The copied collection length.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-02")]
    public int BeginEdit() => _settings.BeginEdit().Value.Values.Count;

    /// <summary>Loads a partial section over configured collection defaults.</summary>
    /// <returns>The preserved collection length.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-03")]
    public int Overlay()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("""{"documentVersion":1,"sections":{"sample":{"schemaVersion":1,"values":{"Number":7}}}}""") };
        using ServiceProvider provider = CreateProvider(store, Items);
        return provider.GetRequiredService<IEditableOptions<BenchmarkModel>>().Current.Value.Values.Count;
    }

    /// <summary>Rejects implicit overwrite of malformed input.</summary>
    /// <returns>The protection status.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-04")]
    public SettingsSaveStatus CorruptionProtection()
    {
        using ServiceProvider provider = CreateProvider(new MemoryStore { Data = "{"u8.ToArray() }, Items);
        IEditableOptions<BenchmarkModel> settings = provider.GetRequiredService<IEditableOptions<BenchmarkModel>>();
        return settings.SaveAsync(settings.BeginEdit()).GetAwaiter().GetResult().Status;
    }

    /// <summary>Round-trips and atomically commits a changing candidate to memory.</summary>
    /// <returns>The commit status.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-05")]
    public SettingsSaveStatus Save()
    {
        SettingsEdit<BenchmarkModel> edit = _settings.BeginEdit();
        edit.Value.Number = edit.Value.Number == 1 ? 2 : 1;
        return _settings.SaveAsync(edit).GetAwaiter().GetResult().Status;
    }

    /// <summary>Rejects invalid values before writing.</summary>
    /// <returns>The validation status.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-06")]
    public SettingsSaveStatus Invalid()
    {
        SettingsEdit<BenchmarkModel> edit = _settings.BeginEdit();
        edit.Value.Number = -1;
        return _settings.SaveAsync(edit).GetAwaiter().GetResult().Status;
    }

    /// <summary>Commits one edit before attempting a stale edit.</summary>
    /// <returns>The stale edit status.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-07")]
    public SettingsSaveStatus Conflict()
    {
        SettingsEdit<BenchmarkModel> stale = _settings.BeginEdit();
        _ = Save();
        return _settings.SaveAsync(stale).GetAwaiter().GetResult().Status;
    }

    /// <summary>Resets the module transactionally.</summary>
    /// <returns>The reset status.</returns>
    [Benchmark]
    [BenchmarkCategory("CORE-SET-08")]
    public SettingsSaveStatus Reset() => _settings.ResetAsync(_settings.Revision).GetAwaiter().GetResult().Status;

    internal static ServiceProvider CreateProvider(ISettingsStore store, int items)
    {
        PersistedSettingsSource source = PersistedSettingsSource.LoadAsync(store).GetAwaiter().GetResult();
        IConfigurationRoot configuration = new ConfigurationBuilder().AddPersistedSettings(source).Build();
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(_ => (IDisposable)configuration);
        services.AddPersistedOptions<BenchmarkModel>("sample")
            .UseJsonTypeInfo(BenchmarkJson.Default.BenchmarkModel)
            .Configure(value => value.Values = Enumerable.Range(0, items).ToList())
            .Validate(value => value.Number >= 0, "Nonnegative scalar required.");
        return services.BuildServiceProvider();
    }

    internal sealed class MemoryStore : ISettingsStore
    {
        internal byte[]? Data { get; set; }

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Data?.ToArray());

        public ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Data = data.ToArray();
            return ValueTask.CompletedTask;
        }
    }
}
