using BenchmarkDotNet.Attributes;
using Lumyte.Settings.Benchmark.PersistedSettings;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Settings.Benchmark.FileSettingsStorage;

/// <summary>Verification helper for DI-backed file persistence and restart; not a measured workload.</summary>
public class FileSettingsBenchmarks
{
    private string _directory = null!;
    private string _path = null!;

    /// <summary>Creates a private temporary directory outside measured operations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lumyte-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    /// <summary>Deletes all owned temporary files.</summary>
    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    /// <summary>Loads a document, saves an edit atomically, and reloads the persisted file.</summary>
    /// <returns>The committed scalar after restart.</returns>
    public int SaveAndReload()
    {
        using (ServiceProvider provider = SettingsBenchmarks.CreateProvider(new JsonFileSettingsStore(_path), 16))
        {
            IEditableOptions<BenchmarkModel> settings = provider.GetRequiredService<IEditableOptions<BenchmarkModel>>();
            SettingsEdit<BenchmarkModel> edit = settings.BeginEdit();
            edit.Value.Number = edit.Value.Number == 1 ? 2 : 1;
            if (settings.SaveAsync(edit).GetAwaiter().GetResult().Status != SettingsSaveStatus.Saved)
            {
                throw new InvalidOperationException("File transaction did not commit.");
            }
        }

        using ServiceProvider restarted = SettingsBenchmarks.CreateProvider(new JsonFileSettingsStore(_path), 16);
        return restarted.GetRequiredService<IEditableOptions<BenchmarkModel>>().Current.Value.Number;
    }
}
