using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Exercises the actual DI, persistence and module registration contracts.</summary>
public sealed class SettingsTests
{
    /// <summary>ConfigurationManager loads files on registration, not Options resolution.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task FileSourceLoadsDuringRegistrationAndSavesForRestartAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, Document("0.3"));
            var configuration = new ConfigurationManager();
            var source = new PersistedJsonFileSource(path);
            configuration.AddPersistedJsonFile(source);
            await File.WriteAllTextAsync(path, Document("0.4"));
            var services = new ServiceCollection();
            services.UseSampleModule();
            services.AddSettings(source);
            using ServiceProvider provider = services.BuildServiceProvider();
            IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
            Assert.Equal(0.3f, settings.Current.Value.PrimaryRange.Minimum);
            SettingsEdit<SampleSettings> edit = settings.BeginEdit();
            edit.Value.PrimaryRange.Minimum = 0.2f;
            edit.Value.Entries["group"] = new() { ["item"] = ["first", "second"] };
            Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
            var next = new ConfigurationBuilder();
            var nextSource = new PersistedJsonFileSource(path);
            next.AddPersistedJsonFile(nextSource);
            Assert.Throws<InvalidOperationException>(() => CreateFileProvider(nextSource).GetRequiredService<IEditableOptions<SampleSettings>>());
            using var root = (IDisposable)next.Build();
            using ServiceProvider restarted = CreateFileProvider(nextSource);
            SampleSettings loaded = restarted.GetRequiredService<IEditableOptions<SampleSettings>>().Current.Value;
            Assert.Equal(0.2f, loaded.PrimaryRange.Minimum);
            Assert.Equal(["first", "second"], loaded.Entries["group"]["item"]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Module registration is idempotent and independent of common source registration.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ModuleRegistrationAndSnapshotsAreIsolatedAsync()
    {
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.UseSampleModule().UseSampleModule());
        Assert.Single(provider.GetServices<IValidateOptions<SampleSettings>>());
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SampleSettings copy = settings.Current.Value;
        copy.PrimaryRange.Minimum = 0.8f;
        copy.Entries["fake"] = [];
        Assert.Equal(0.15f, settings.Current.Value.PrimaryRange.Minimum);
        Assert.Empty(settings.Current.Value.Entries);
        provider.GetRequiredService<IOptions<SampleSettings>>().Value.PrimaryRange.Minimum = 0.7f;
        Assert.Equal(0.15f, settings.Current.Value.PrimaryRange.Minimum);
        Assert.Equal(SettingsLoadStatus.Defaults, settings.LoadResult.Status);
        Assert.Equal(1, store.Reads);
    }

    /// <summary>Unreferenced module settings are validated before Host startup completes.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task HostStartupAutomaticallyValidatesModulesAsync()
    {
        var store = new MemoryStore();
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        using IHost host = new HostBuilder().ConfigureAppConfiguration(configuration => configuration.Add(source)).ConfigureServices(services =>
        {
            services.AddSettings(source).UseSampleModule();
            services.Configure<SampleSettings>(value => value.PrimaryRange.Minimum = float.NaN);
        }).Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.Equal(0, store.Writes);
    }

    /// <summary>Nonfinite and null candidates reach standard validation without JSON exceptions.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task InvalidCandidatesAreDiagnosedBeforeSerializationAsync()
    {
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SettingsEdit<SampleSettings> edit = settings.BeginEdit();
        edit.Value.PrimaryRange.Minimum = float.NaN;
        edit.Value.SecondaryRange.Maximum = float.PositiveInfinity;
        edit.Value.Entries = null!;
        SettingsSaveResult<SampleSettings> result = await settings.SaveAsync(edit);
        Assert.Equal(SettingsSaveStatus.ValidationFailed, result.Status);
        Assert.Equal(3, result.Errors.Length);
        Assert.Equal(0, settings.Revision);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>Only stale edits in the same module conflict; other modules and unknown sections survive.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ConcurrentModulesPreserveOtherSectionsAndDetectStaleEditsAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"future\":{\"schemaVersion\":99,\"values\":{\"keep\":[1,2]}}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        SettingsEdit<SampleSettings> edit = sample.BeginEdit();
        SettingsEdit<SampleSettings> stale = sample.BeginEdit();
        SettingsEdit<OtherSettings> otherEdit = other.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.25f;
        otherEdit.Value.Number = 42;
        store.PauseWrites = true;
        Task<SettingsSaveResult<SampleSettings>> first = sample.SaveAsync(edit);
        await store.Entered.Task;
        Task<SettingsSaveResult<OtherSettings>> second = other.SaveAsync(otherEdit);
        Assert.False(second.IsCompleted);
        store.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(SettingsSaveStatus.Conflict, (await sample.SaveAsync(stale)).Status);
        JsonNode document = JsonNode.Parse(store.Data!)!;
        Assert.Equal(42, document["sections"]!["other"]!["values"]!["number"]!.GetValue<int>());
        Assert.Equal(0.25f, document["sections"]!["sample"]!["values"]!["primaryRange"]!["minimum"]!.GetValue<float>());
        Assert.Equal(99, document["sections"]!["future"]!["schemaVersion"]!.GetValue<int>());
        Assert.Equal(1, sample.Revision);
        Assert.Equal(1, other.Revision);
    }

    /// <summary>Saving one module does not invalidate another module's open edit.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task AnotherModuleDoesNotConflictAndFailurePreservesRevisionAsync()
    {
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        SettingsEdit<OtherSettings> edit = other.BeginEdit();
        await sample.SaveAsync(sample.BeginEdit());
        Assert.Equal(SettingsSaveStatus.Saved, (await other.SaveAsync(edit)).Status);
        store.FailWrites = true;
        SettingsEdit<SampleSettings> retry = sample.BeginEdit();
        retry.Value.PrimaryRange.Minimum = 0.6f;
        Assert.Equal(SettingsSaveStatus.StorageFailure, (await sample.SaveAsync(retry)).Status);
        Assert.Equal(1, sample.Revision);
        Assert.Equal(0.15f, sample.Current.Value.PrimaryRange.Minimum);
        store.FailWrites = false;
        Assert.Equal(SettingsSaveStatus.Saved, (await sample.SaveAsync(retry)).Status);
    }

    /// <summary>Document corruption requires explicit whole-document recovery.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task DocumentCorruptionProtectsWritesUntilExplicitRecoveryAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("broken") };
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsLoadStatus.InvalidData, settings.LoadResult.Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.ResetAsync(0)).Status);
        store.FailWrites = true;
        ISettingsDocument document = provider.GetRequiredService<ISettingsDocument>();
        Assert.Equal(SettingsSaveStatus.StorageFailure, (await document.ResetAsync()).Status);
        Assert.Equal(0, settings.Revision);
        store.FailWrites = false;
        SettingsEdit<SampleSettings> stale = settings.BeginEdit();
        Assert.Equal(SettingsSaveStatus.Saved, (await document.ResetAsync()).Status);
        Assert.Equal(SettingsSaveStatus.Conflict, (await settings.SaveAsync(stale)).Status);
    }

    /// <summary>A corrupt section can be reset without erasing an unrelated section.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task InvalidSectionIsProtectedButOtherModulesCanSaveAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":99,\"values\":{}},\"future\":{\"opaque\":true}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsLoadStatus.UnsupportedVersion, sample.LoadResult.Status);
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        Assert.Equal(SettingsSaveStatus.Saved, (await other.SaveAsync(other.BeginEdit())).Status);
        Assert.Equal(99, JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["schemaVersion"]!.GetValue<int>());
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await sample.SaveAsync(sample.BeginEdit())).Status);
        Assert.Equal(SettingsSaveStatus.Saved, (await sample.ResetAsync(sample.Revision)).Status);
        Assert.True(JsonNode.Parse(store.Data!)!["sections"]!["future"]!["opaque"]!.GetValue<bool>());
    }

    /// <summary>Arrays and dictionaries replace defaults while object properties are completed.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task EmptyAndDeletedDictionaryEntriesAreNotRestoredAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":{\"entries\":{\"group\":{\"item\":[]}},\"primaryRange\":{\"minimum\":0}}}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.Configure<SampleSettings>(value => value.Entries["group"] = new() { ["item"] = ["first"], ["removed"] = ["third"] }));
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Empty(sample.Current.Value.Entries["group"]["item"]);
        Assert.False(sample.Current.Value.Entries["group"].ContainsKey("removed"));
        Assert.Equal(0, sample.Current.Value.PrimaryRange.Minimum);
        Assert.Equal(1, sample.Current.Value.PrimaryRange.Maximum);
        await sample.SaveAsync(sample.BeginEdit());
        using ServiceProvider restarted = await CreateProviderAsync(store, services => services.Configure<SampleSettings>(value => value.Entries["group"] = new() { ["removed"] = ["third"] }));
        Assert.False(restarted.GetRequiredService<IEditableOptions<SampleSettings>>().Current.Value.Entries["group"].ContainsKey("removed"));
    }

    /// <summary>Candidate capture precedes asynchronous storage and committed cancellation is not failure.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task CandidateIsCapturedBeforeAwaitAndCommitIgnoresLateCancellationAsync()
    {
        var store = new MemoryStore { PauseWrites = true };
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SettingsEdit<SampleSettings> edit = sample.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.2f;
        Task<SettingsSaveResult<SampleSettings>> save = sample.SaveAsync(edit);
        await store.Entered.Task;
        edit.Value.PrimaryRange.Minimum = 0.8f;
        store.Release.SetResult();
        Assert.Equal(SettingsSaveStatus.Saved, (await save).Status);
        Assert.Equal(0.2f, sample.Current.Value.PrimaryRange.Minimum);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sample.SaveAsync(sample.BeginEdit(), cancellation.Token));
        Assert.Equal(1, sample.Revision);
        store.PauseWrites = false;
        store.AfterCommit = cancellation.Cancel;
        using var late = new CancellationTokenSource();
        store.AfterCommit = late.Cancel;
        Assert.Equal(SettingsSaveStatus.Saved, (await sample.SaveAsync(sample.BeginEdit(), late.Token)).Status);
        Assert.True(late.IsCancellationRequested);
        Assert.Equal(2, sample.Revision);
    }

    /// <summary>Standard Options retain their cache while editable snapshots track committed revisions.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task StandardOptionsCacheRemainsIndependentOfCommittedSnapshotsAsync()
    {
        using ServiceProvider provider = await CreateProviderAsync(new MemoryStore());
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SampleSettings cached = provider.GetRequiredService<IOptions<SampleSettings>>().Value;
        SettingsSnapshot<SampleSettings> snapshot = settings.Current;
        SettingsEdit<SampleSettings> edit = settings.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.2f;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        Assert.Equal(0.15f, cached.PrimaryRange.Minimum);
        Assert.Equal(0.15f, snapshot.Value.PrimaryRange.Minimum);
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal(0.2f, settings.Current.Value.PrimaryRange.Minimum);
        Assert.Equal(1, settings.Revision);
    }

    /// <summary>Section IDs and common sources cannot be accidentally assigned twice.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ConflictingRegistrationsAreRejectedAsync()
    {
        PersistedSettingsSource first = await PersistedSettingsSource.LoadAsync(new MemoryStore());
        PersistedSettingsSource second = await PersistedSettingsSource.LoadAsync(new MemoryStore());
        var services = new ServiceCollection();
        services.AddSettings(first).AddSettings(first).UseSampleModule();
        Assert.Throws<InvalidOperationException>(() => services.AddSettings(second));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<OtherSettings>("sample"));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<SampleSettings>("renamed"));
    }

    /// <summary>Old sections migrate in memory without rewriting on read.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task MigrationIsDeferredAndUnknownPropertiesProtectOnlyTheirModuleAsync()
    {
        var store = new MemoryStore
        {
            Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"other\":{\"schemaVersion\":1,\"values\":{\"number\":2}},\"sample\":{\"schemaVersion\":1,\"values\":{\"typo\":true}}}}"),
        };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(12, other.Current.Value.Number);
        Assert.Equal(SettingsLoadStatus.InvalidData, sample.LoadResult.Status);
        Assert.Equal(0, store.Writes);
        await other.SaveAsync(other.BeginEdit());
        Assert.Equal(2, JsonNode.Parse(store.Data!)!["sections"]!["other"]!["schemaVersion"]!.GetValue<int>());
        Assert.True(JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["values"]!["typo"]!.GetValue<bool>());
    }

    /// <summary>Default configuration, normalization and multiple standard validators run in order.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task StandardOptionsPipelineAndHostlessStartupAreSharedAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes(Document("0.3")) };
        using ServiceProvider provider = await CreateProviderAsync(store, services =>
        {
            services.PostConfigure<SampleSettings>(value => value.PrimaryRange.Minimum = Math.Clamp(value.PrimaryRange.Minimum, 0, 0.5f));
            services.AddOptions<SampleSettings>().Validate(value => value.PrimaryRange.Minimum <= 0.5f, "normalized");
            services.AddOptions<SampleSettings>().Validate(value => value.PrimaryRange.Maximum == 1, "maximum");
        });
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
        Assert.Equal(0.3f, provider.GetRequiredService<IOptionsMonitor<SampleSettings>>().CurrentValue.PrimaryRange.Minimum);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SettingsEdit<SampleSettings> edit = settings.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.8f;
        await settings.SaveAsync(edit);
        Assert.Equal(0.5f, settings.Current.Value.PrimaryRange.Minimum);
        Assert.Equal(0.8f, edit.Value.PrimaryRange.Minimum);
        SettingsEdit<SampleSettings> invalid = settings.BeginEdit();
        invalid.Value.PrimaryRange.Maximum = 0.9f;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(invalid)).Status);
    }

    /// <summary>Duplicate JSON properties are diagnosed instead of escaping during lazy parsing.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task DuplicateJsonPropertiesProtectTheOriginalDocumentAsync()
    {
        var store = new MemoryStore
        {
            Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":{\"entries\":{\"group\":{},\"group\":{}}}}}}"),
        };
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsLoadStatus.InvalidData, sample.LoadResult.Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await sample.SaveAsync(sample.BeginEdit())).Status);
        Assert.Equal(0, store.Writes);
    }

    private static ServiceProvider CreateFileProvider(PersistedSettingsSource source) => new ServiceCollection().AddSettings(source).UseSampleModule().BuildServiceProvider();

    private static async Task<ServiceProvider> CreateProviderAsync(MemoryStore store, Action<IServiceCollection>? configure = null)
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var configuration = new ConfigurationManager();
        configuration.AddPersistedSettings(source);
        var services = new ServiceCollection();
        services.UseSampleModule();
        configure?.Invoke(services);
        services.AddSettings(source);
        return services.BuildServiceProvider();
    }

    private static string Document(string minimum) => "{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":{\"primaryRange\":{\"minimum\":" + minimum + "}}}}}";

    private sealed class MemoryStore : ISettingsStore
    {
        public byte[]? Data { get; set; }

        public bool FailWrites { get; set; }

        public bool PauseWrites { get; set; }

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Action? AfterCommit { get; set; }

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return ValueTask.FromResult(Data?.ToArray());
        }

        public async ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (FailWrites)
            {
                throw new IOException("Injected failure.");
            }

            if (PauseWrites)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Data = data.ToArray();
            AfterCommit?.Invoke();
        }
    }
}
