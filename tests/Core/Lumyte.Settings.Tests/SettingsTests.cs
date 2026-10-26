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
    public async Task MigrationIsDeferredAndUnknownPropertiesAreRemovedOnSaveAsync()
    {
        var store = new MemoryStore
        {
            Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"other\":{\"schemaVersion\":1,\"values\":{\"number\":2}},\"sample\":{\"schemaVersion\":1,\"values\":{\"typo\":true}}}}"),
        };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        IEditableOptions<SampleSettings> sample = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(12, other.Current.Value.Number);
        Assert.Equal(SettingsLoadStatus.Loaded, sample.LoadResult.Status);
        Assert.Equal(0, store.Writes);
        await other.SaveAsync(other.BeginEdit());
        Assert.Equal(2, JsonNode.Parse(store.Data!)!["sections"]!["other"]!["schemaVersion"]!.GetValue<int>());
        Assert.True(JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["values"]!["typo"]!.GetValue<bool>());
        await sample.SaveAsync(sample.BeginEdit());
        Assert.Null(JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["values"]!["typo"]);
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

    /// <summary>Reflection defaults require no definition or explicit metadata registration.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ReflectionRegistrationPersistsWithoutADefinitionAsync()
    {
        var store = new MemoryStore();
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var configuration = new ConfigurationManager();
        configuration.AddPersistedSettings(source);
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddPersistedOptions<SampleSettings>("sample");
        using ServiceProvider provider = services.BuildServiceProvider();
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        SettingsEdit<SampleSettings> edit = settings.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.3f;
        edit.Value.Entries["group"] = new() { ["item"] = ["first"] };
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        edit.Value.Entries["group"]["item"][0] = "changed";
        Assert.Equal("first", settings.Current.Value.Entries["group"]["item"][0]);
        using ServiceProvider restarted = await CreateProviderAsync(store);
        Assert.Equal(0.3f, restarted.GetRequiredService<IEditableOptions<SampleSettings>>().Current.Value.PrimaryRange.Minimum);
        Assert.Equal(1, JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>Metadata copies nested arrays, lists, nullable values and shared references without JSON.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task MetadataCopyPreservesInvalidValuesAndSeparatesCollectionsAsync()
    {
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<CollectionSettings>("collections")
            .UseJsonTypeInfo(TestJsonContext.Default.CollectionSettings)
            .Validate(value => value.Optional is null || float.IsFinite(value.Optional.Value), "finite"));
        IEditableOptions<CollectionSettings> settings = provider.GetRequiredService<IEditableOptions<CollectionSettings>>();
        var range = new SampleRange { Minimum = 0.2f };
        SettingsEdit<CollectionSettings> edit = settings.BeginEdit();
        edit.Value.Ranges = [range];
        edit.Value.Bytes = [1, 2];
        edit.Value.Array = [range];
        edit.Value.Optional = float.NaN;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(edit)).Status);
        Assert.Equal(0, store.Writes);
        edit.Value.Optional = null;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        range.Minimum = 0.8f;
        edit.Value.Bytes[0] = 9;
        CollectionSettings snapshot = settings.Current.Value;
        Assert.Equal(0.2f, snapshot.Ranges[0].Minimum);
        Assert.Equal(1, snapshot.Bytes[0]);
        snapshot.Bytes[0] = 8;
        Assert.Equal(1, settings.Current.Value.Bytes[0]);
        Assert.Same(snapshot.Ranges[0], snapshot.Array[0]);
        snapshot.Array[0].Minimum = 0.9f;
        Assert.Equal(0.2f, settings.Current.Value.Array[0].Minimum);
        using ServiceProvider restarted = await CreateProviderAsync(store, services => services.AddPersistedOptions<CollectionSettings>("collections").UseJsonTypeInfo(TestJsonContext.Default.CollectionSettings));
        Assert.Null(restarted.GetRequiredService<IEditableOptions<CollectionSettings>>().Current.Value.Optional);
    }

    /// <summary>Migration can be registered as a delegate instead of implementing a definition.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task MetadataMigrationRunsOnlyForOlderVersionsAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"other\":{\"schemaVersion\":1,\"values\":{\"number\":2}}}}") };
        int migrations = 0;
        JsonObject Upgrade(JsonObject values, int version)
        {
            Assert.Equal(1, version);
            migrations++;
            values["number"] = values["number"]!.GetValue<int>() + 10;
            return values;
        }

        void Register(IServiceCollection services) => services.AddPersistedOptions<OtherSettings>("other").UseJsonTypeInfo(TestJsonContext.Default.OtherSettings, schemaVersion: 2, upgrade: Upgrade);
        using ServiceProvider provider = await CreateProviderAsync(store, Register);
        IEditableOptions<OtherSettings> settings = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        Assert.Equal(12, settings.Current.Value.Number);
        Assert.Equal(0, store.Writes);
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(settings.BeginEdit())).Status);
        using ServiceProvider restarted = await CreateProviderAsync(store, Register);
        Assert.Equal(12, restarted.GetRequiredService<IEditableOptions<OtherSettings>>().Current.Value.Number);
        Assert.Equal(1, migrations);
    }

    /// <summary>An old section without a migration is protected instead of silently rewritten.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task MissingMetadataMigrationProtectsSavedValuesAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"other\":{\"schemaVersion\":1,\"values\":{\"number\":2}}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonTypeInfo(TestJsonContext.Default.OtherSettings, schemaVersion: 2));
        IEditableOptions<OtherSettings> settings = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        Assert.Equal(SettingsLoadStatus.UnsupportedVersion, settings.LoadResult.Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Equal(0, store.Writes);
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.ResetAsync(settings.Revision)).Status);
        Assert.Equal(2, JsonNode.Parse(store.Data!)!["sections"]!["other"]!["schemaVersion"]!.GetValue<int>());
    }

    /// <summary>Unsupported shapes and ambiguous registrations fail before module use.</summary>
    [Fact]
    public void MetadataRegistrationRejectsUnsupportedShapesAndConflicts()
    {
        var services = new ServiceCollection();
        OptionsBuilder<ReadOnlySettings> unsupported = services.AddPersistedOptions<ReadOnlySettings>("readonly");
        Assert.Throws<InvalidOperationException>(() => unsupported.UseJsonTypeInfo(TestJsonContext.Default.ReadOnlySettings));
        OptionsBuilder<SampleSettings> builder = services.AddPersistedOptions<SampleSettings>("sample");
        builder.UseJsonTypeInfo(TestJsonContext.Default.SampleSettings);
        builder.UseJsonTypeInfo(TestJsonContext.Default.SampleSettings);
        Assert.Single(services, item => item.ServiceType == typeof(ISettingsDefinition<SampleSettings>));
        Assert.Throws<InvalidOperationException>(() => builder.UseJsonTypeInfo(TestJsonContext.Default.SampleSettings, schemaVersion: 2));
        OptionsBuilder<OtherSettings> custom = services.AddPersistedOptions<OtherSettings>("other");
        custom.UseJsonDefinition<OtherSettings, OtherDefinition>();
        Assert.Throws<InvalidOperationException>(() => custom.UseJsonTypeInfo(TestJsonContext.Default.OtherSettings));
        Assert.Throws<InvalidOperationException>(() => services.AddOptions<CollectionSettings>("named").UseJsonTypeInfo(TestJsonContext.Default.CollectionSettings));
    }

    /// <summary>Saved values are applied before PostConfigure without serializing unnormalized defaults.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task NormalizedDefaultsCanBeSavedAndReloadedAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"sample\":{\"schemaVersion\":1,\"values\":{}}}}") };
        void Register(IServiceCollection services)
        {
            services.Configure<SampleSettings>(value => value.PrimaryRange.Minimum = float.NaN);
            services.PostConfigure<SampleSettings>(value => value.PrimaryRange.Minimum = float.IsFinite(value.PrimaryRange.Minimum) ? value.PrimaryRange.Minimum : 0.2f);
        }

        using ServiceProvider provider = await CreateProviderAsync(store, Register);
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, settings.LoadResult.Status);
        Assert.Equal(0.2f, settings.Current.Value.PrimaryRange.Minimum);
        SettingsEdit<SampleSettings> edit = settings.BeginEdit();
        edit.Value.PrimaryRange.Minimum = 0.7f;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        using ServiceProvider restarted = await CreateProviderAsync(store, Register);
        IEditableOptions<SampleSettings> loaded = restarted.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.LoadResult.Status);
        Assert.Equal(0.7f, loaded.Current.Value.PrimaryRange.Minimum);
    }

    /// <summary>Configuration callbacks cannot retain mutable aliases after save or either reset.</summary>
    /// <param name="operation">Zero saves, one resets a section, and two resets the document.</param>
    /// <param name="postConfigure">Whether the callback runs after the candidate is captured.</param>
    /// <returns>The asynchronous test operation.</returns>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task CommittedValuesNeverAliasCallbackCollectionsAsync(int operation, bool postConfigure)
    {
        var shared = new Dictionary<string, Dictionary<string, string[]>> { ["group"] = new() { ["item"] = ["before"] } };
        var store = new MemoryStore { PauseWrites = true };
        using ServiceProvider provider = await CreateProviderAsync(store, services =>
        {
            if (postConfigure)
            {
                services.PostConfigure<SampleSettings>(value => value.Entries = shared);
            }
            else
            {
                services.Configure<SampleSettings>(value => value.Entries = shared);
            }
        });
        IEditableOptions<SampleSettings> settings = provider.GetRequiredService<IEditableOptions<SampleSettings>>();
        Assert.Equal("before", settings.Current.Value.Entries["group"]["item"][0]);
        shared["group"]["item"][0] = "captured";
        Assert.Equal("before", settings.Current.Value.Entries["group"]["item"][0]);
        async Task<SettingsSaveStatus> CommitAsync()
        {
            if (operation == 2)
            {
                return (await provider.GetRequiredService<ISettingsDocument>().ResetAsync()).Status;
            }

            SettingsSaveResult<SampleSettings> result = operation == 0
                ? await settings.SaveAsync(settings.BeginEdit())
                : await settings.ResetAsync(settings.Revision);
            return result.Status;
        }

        Task<SettingsSaveStatus> commit = CommitAsync();
        await store.Entered.Task;
        shared["group"]["item"][0] = "after";
        store.Release.SetResult();
        Assert.Equal(SettingsSaveStatus.Saved, await commit);
        Assert.Equal(1, settings.Revision);
        Assert.Equal("captured", settings.Current.Value.Entries["group"]["item"][0]);
        Assert.Equal("captured", JsonNode.Parse(store.Data!)!["sections"]!["sample"]!["values"]!["entries"]!["group"]!["item"]![0]!.GetValue<string>());
    }

    /// <summary>A serializer which writes a shape its reader rejects must never commit.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task UnreadableSerializerOutputIsRejectedBeforeWritingAsync()
    {
        var context = new TestJsonContext(new System.Text.Json.JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.WriteAsString,
        });
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonTypeInfo(context.OtherSettings));
        IEditableOptions<OtherSettings> settings = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        SettingsEdit<OtherSettings> edit = settings.BeginEdit();
        edit.Value.Number = 5;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(edit)).Status);
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await provider.GetRequiredService<ISettingsDocument>().ResetAsync()).Status);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, settings.Revision);
        Assert.Equal(0, settings.Current.Value.Number);
    }

    /// <summary>The full document must fit the reader's depth limit before writing.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task UnreadableDepthIsRejectedBeforeSaveOrDocumentResetAsync()
    {
        var context = new TestJsonContext(new System.Text.Json.JsonSerializerOptions { MaxDepth = 128 });
        bool deepDefaults = false;
        var store = new MemoryStore();
        void Register(IServiceCollection services) => services.AddPersistedOptions<RecursiveSettings>("recursive")
            .UseJsonTypeInfo(context.RecursiveSettings)
            .Configure(value => value.Child = deepDefaults ? Chain(70) : null);
        using ServiceProvider provider = await CreateProviderAsync(store, Register);
        IEditableOptions<RecursiveSettings> settings = provider.GetRequiredService<IEditableOptions<RecursiveSettings>>();
        SettingsEdit<RecursiveSettings> edit = settings.BeginEdit();
        edit.Value.Child = Chain(70);
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(edit)).Status);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, settings.Revision);
        edit.Value.Child = Chain(30);
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        byte[] saved = store.Data!.ToArray();
        deepDefaults = true;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await provider.GetRequiredService<ISettingsDocument>().ResetAsync()).Status);
        Assert.Equal(1, store.Writes);
        Assert.Equal(1, settings.Revision);
        Assert.Equal(saved, store.Data);
        deepDefaults = false;
        using ServiceProvider restarted = await CreateProviderAsync(store, Register);
        IEditableOptions<RecursiveSettings> loaded = restarted.GetRequiredService<IEditableOptions<RecursiveSettings>>();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.LoadResult.Status);
        Assert.NotNull(loaded.Current.Value.Child);

        static RecursiveSettings Chain(int depth)
        {
            var root = new RecursiveSettings();
            RecursiveSettings current = root;
            for (int index = 1; index < depth; index++)
            {
                current.Child = new();
                current = current.Child;
            }

            return root;
        }
    }

    /// <summary>Replacing dictionary entries on load retains the configured comparison rules.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ReloadRetainsDictionaryComparerWithoutRestoringDeletedKeysAsync()
    {
        var store = new MemoryStore();
        void Register(IServiceCollection services) => services.AddPersistedOptions<DictionaryCopySettings>("dictionary")
            .UseJsonTypeInfo(DictionaryCopyJsonContext.Default.DictionaryCopySettings)
            .Configure(value => value.Counts = new(StringComparer.OrdinalIgnoreCase) { ["Removed"] = 1 });
        using ServiceProvider provider = await CreateProviderAsync(store, Register);
        IEditableOptions<DictionaryCopySettings> settings = provider.GetRequiredService<IEditableOptions<DictionaryCopySettings>>();
        SettingsEdit<DictionaryCopySettings> edit = settings.BeginEdit();
        edit.Value.Counts.Clear();
        edit.Value.Counts["MixedCase"] = 7;
        Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
        using ServiceProvider restarted = await CreateProviderAsync(store, Register);
        DictionaryCopySettings loaded = restarted.GetRequiredService<IEditableOptions<DictionaryCopySettings>>().Current.Value;
        Assert.Equal(7, loaded.Counts["mixedcase"]);
        Assert.False(loaded.Counts.ContainsKey("Removed"));
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
