using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;

namespace Lumyte.Settings.Benchmark.PersistedSettings;

/// <summary>Public persisted source JSON loading and provider flattening without DI registration.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class PersistedSettingsSourceBenchmarks
{
    private PayloadStore _store = null!;
    private IConfigurationProvider _provider = null!;

    /// <summary>Gets or sets the section array length.</summary>
    [Params(1, 16, 256)]
    public int Items { get; set; } = 16;

    /// <summary>Gets or sets a valid, duplicate-property, unsupported-version or absent envelope.</summary>
    [Params("Valid", "Duplicate", "Unsupported", "Missing")]
    public string Profile { get; set; } = "Valid";

    /// <summary>Prepares immutable encoded bytes and a provider outside measured operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        string values = JsonSerializer.Serialize(Enumerable.Range(0, Items).ToArray());
        string json = Profile == "Duplicate" ? "{\"documentVersion\":1,\"documentVersion\":1,\"sections\":{}}"
            : "{\"documentVersion\":" + (Profile == "Unsupported" ? "2" : "1") + ",\"sections\":{\"sample\":{\"version\":1,\"values\":{\"number\":17,\"items\":" + values + "}}}}";
        _store = new PayloadStore(Profile == "Missing" ? null : Encoding.UTF8.GetBytes(json));
        PersistedSettingsSource source = Load().GetAwaiter().GetResult();
        _provider = source.Build(new ConfigurationBuilder());
        _provider.Load();
    }

    /// <summary>Reads and validates a fresh settings envelope, including recovery-to-default branches.</summary>
    /// <returns>The actual product source; no independent parser is substituted.</returns>
    [Benchmark]
    public Task<PersistedSettingsSource> Load() => PersistedSettingsSource.LoadAsync(_store);

    /// <summary>Republishes parsed nested section values into the source's own configuration provider.</summary>
    /// <returns>The last section array element, or null for recovered/default envelopes.</returns>
    [Benchmark]
    public string? FlattenSections()
    {
        _provider.Load();
        _provider.TryGet("sections:sample:values:items:" + (Items - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), out string? value);
        return value;
    }

    private sealed class PayloadStore(byte[]? bytes) : ISettingsStore
    {
        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(bytes);

        public ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
