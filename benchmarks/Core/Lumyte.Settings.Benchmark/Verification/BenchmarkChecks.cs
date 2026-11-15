using Lumyte.Settings.Benchmark.FileSettingsStorage;
using Lumyte.Settings.Benchmark.PersistedSettings;

namespace Lumyte.Settings.Benchmark;

/// <summary>Deterministic settings benchmark oracles.</summary>
public static class BenchmarkChecks
{
    /// <summary>Checks copy boundaries, failure statuses, and actual file round trips.</summary>
    public static void Verify()
    {
        foreach (int items in new[] { 0, 16, 256 })
        {
            var benchmark = new SettingsBenchmarks { Items = items };
            benchmark.Setup();
            try
            {
                benchmark.RegisterAndValidate();
                if (benchmark.Snapshot() != items || benchmark.BeginEdit() != items || benchmark.Overlay() != items
                    || benchmark.CorruptionProtection() != SettingsSaveStatus.RecoveryRequired
                    || benchmark.Save() != SettingsSaveStatus.Saved || benchmark.Invalid() != SettingsSaveStatus.ValidationFailed
                    || benchmark.Conflict() != SettingsSaveStatus.Conflict || benchmark.Reset() != SettingsSaveStatus.Saved)
                {
                    throw new InvalidOperationException("Settings memory contract failed.");
                }
            }
            finally
            {
                benchmark.Cleanup();
            }
        }

        var file = new FileSettingsBenchmarks();
        file.Setup();
        try
        {
            if (file.SaveAndReload() != 2 || file.SaveAndReload() != 1)
            {
                throw new InvalidOperationException("File persistence contract failed.");
            }
        }
        finally
        {
            file.Cleanup();
        }
    }
}
