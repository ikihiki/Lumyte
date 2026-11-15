using BenchmarkDotNet.Attributes;

namespace Lumyte.Settings.Benchmark.FileSettingsStorage;

/// <summary>Direct store operations without a service provider or settings integration.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class JsonFileSettingsStoreBenchmarks
{
    private string _directory = string.Empty;
    private JsonFileSettingsStore _store = null!;
    private JsonFileSettingsStore _missing = null!;
    private byte[] _first = [];
    private byte[] _second = [];
    private bool _alternate;

    /// <summary>Gets or sets payload size in bytes.</summary>
    [Params(64, 4096, 65536)]
    public int Bytes { get; set; } = 64;

    /// <summary>Seeds a real file and a distinct absent path outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lumyte-json-file-settings-store-" + Guid.NewGuid().ToString("N"));
        _store = new JsonFileSettingsStore(Path.Combine(_directory, "settings.json"));
        _missing = new JsonFileSettingsStore(Path.Combine(_directory, "absent.json"));
        _first = Enumerable.Range(0, Bytes).Select(index => (byte)(index % 251)).ToArray();
        _second = _first.Select(value => (byte)(value ^ 0x5a)).ToArray();
        _store.WriteAtomicallyAsync(_first).GetAwaiter().GetResult();
    }

    /// <summary>Reads a pre-existing payload; parsing is intentionally not included.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public ValueTask<byte[]?> Read() => _store.ReadAsync();

    /// <summary>Reads an absent path through the documented null-result branch.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public ValueTask<byte[]?> Missing() => _missing.ReadAsync();

    /// <summary>Replaces one existing file with alternating nonidentical payloads.</summary>
    /// <returns>The actual operation result.</returns>
    [Benchmark]
    public ValueTask Write()
    {
        _alternate = !_alternate;
        return _store.WriteAtomicallyAsync(_alternate ? _second : _first);
    }

    /// <summary>Removes only the fixture-owned directory.</summary>
    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);
}
