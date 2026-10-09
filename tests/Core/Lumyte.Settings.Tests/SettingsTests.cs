using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Lumyte.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lumyte.Settings.Tests;

/// <summary>Exercises the actual DI, persistence and Input integration contracts.</summary>
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
            services.UseInput();
            services.AddSettings(source);
            using ServiceProvider provider = services.BuildServiceProvider();
            IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
            Assert.Equal(0.3f, settings.Current.Value.LeftStick.Inner);
            SettingsEdit<InputSettings> edit = settings.BeginEdit();
            edit.Value.LeftStick.Inner = 0.2f;
            edit.Value.Bindings["game"] = new() { ["jump"] = ["key:Space", "controller:South"] };
            Assert.Equal(SettingsSaveStatus.Saved, (await settings.SaveAsync(edit)).Status);
            var next = new ConfigurationBuilder();
            var nextSource = new PersistedJsonFileSource(path);
            next.AddPersistedJsonFile(nextSource);
            Assert.Throws<InvalidOperationException>(() => CreateFileProvider(nextSource).GetRequiredService<IEditableOptions<InputSettings>>());
            using var root = (IDisposable)next.Build();
            using ServiceProvider restarted = CreateFileProvider(nextSource);
            InputSettings loaded = restarted.GetRequiredService<IEditableOptions<InputSettings>>().Current.Value;
            Assert.Equal(0.2f, loaded.LeftStick.Inner);
            Assert.Equal(["key:Space", "controller:South"], loaded.Bindings["game"]["jump"]);
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
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.UseInput().UseInput());
        Assert.Single(provider.GetServices<IValidateOptions<InputSettings>>());
        IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        InputSettings copy = settings.Current.Value;
        copy.LeftStick.Inner = 0.8f;
        copy.Bindings["fake"] = [];
        Assert.Equal(0.15f, settings.Current.Value.LeftStick.Inner);
        Assert.Empty(settings.Current.Value.Bindings);
        provider.GetRequiredService<IOptions<InputSettings>>().Value.LeftStick.Inner = 0.7f;
        Assert.Equal(0.15f, settings.Current.Value.LeftStick.Inner);
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
            services.AddSettings(source).UseInput();
            services.Configure<InputSettings>(value => value.LeftStick.Inner = float.NaN);
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
        IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        SettingsEdit<InputSettings> edit = settings.BeginEdit();
        edit.Value.LeftStick.Inner = float.NaN;
        edit.Value.RightStick.Outer = float.PositiveInfinity;
        edit.Value.Bindings = null!;
        SettingsSaveResult<InputSettings> result = await settings.SaveAsync(edit);
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
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        SettingsEdit<InputSettings> edit = input.BeginEdit();
        SettingsEdit<InputSettings> stale = input.BeginEdit();
        SettingsEdit<OtherSettings> otherEdit = other.BeginEdit();
        edit.Value.LeftStick.Inner = 0.25f;
        otherEdit.Value.Number = 42;
        store.PauseWrites = true;
        Task<SettingsSaveResult<InputSettings>> first = input.SaveAsync(edit);
        await store.Entered.Task;
        Task<SettingsSaveResult<OtherSettings>> second = other.SaveAsync(otherEdit);
        Assert.False(second.IsCompleted);
        store.Release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(SettingsSaveStatus.Conflict, (await input.SaveAsync(stale)).Status);
        JsonNode document = JsonNode.Parse(store.Data!)!;
        Assert.Equal(42, document["sections"]!["other"]!["values"]!["number"]!.GetValue<int>());
        Assert.Equal(0.25f, document["sections"]!["input"]!["values"]!["leftStick"]!["inner"]!.GetValue<float>());
        Assert.Equal(99, document["sections"]!["future"]!["schemaVersion"]!.GetValue<int>());
        Assert.Equal(1, input.Revision);
        Assert.Equal(1, other.Revision);
    }

    /// <summary>Saving one module does not invalidate another module's open edit.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task AnotherModuleDoesNotConflictAndFailurePreservesRevisionAsync()
    {
        var store = new MemoryStore();
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        SettingsEdit<OtherSettings> edit = other.BeginEdit();
        await input.SaveAsync(input.BeginEdit());
        Assert.Equal(SettingsSaveStatus.Saved, (await other.SaveAsync(edit)).Status);
        store.FailWrites = true;
        SettingsEdit<InputSettings> retry = input.BeginEdit();
        retry.Value.LeftStick.Inner = 0.6f;
        Assert.Equal(SettingsSaveStatus.StorageFailure, (await input.SaveAsync(retry)).Status);
        Assert.Equal(1, input.Revision);
        Assert.Equal(0.15f, input.Current.Value.LeftStick.Inner);
        store.FailWrites = false;
        Assert.Equal(SettingsSaveStatus.Saved, (await input.SaveAsync(retry)).Status);
    }

    /// <summary>Document corruption requires explicit whole-document recovery.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task DocumentCorruptionProtectsWritesUntilExplicitRecoveryAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("broken") };
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        Assert.Equal(SettingsLoadStatus.InvalidData, settings.LoadResult.Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.SaveAsync(settings.BeginEdit())).Status);
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await settings.ResetAsync(0)).Status);
        store.FailWrites = true;
        ISettingsDocument document = provider.GetRequiredService<ISettingsDocument>();
        Assert.Equal(SettingsSaveStatus.StorageFailure, (await document.ResetAsync()).Status);
        Assert.Equal(0, settings.Revision);
        store.FailWrites = false;
        SettingsEdit<InputSettings> stale = settings.BeginEdit();
        Assert.Equal(SettingsSaveStatus.Saved, (await document.ResetAsync()).Status);
        Assert.Equal(SettingsSaveStatus.Conflict, (await settings.SaveAsync(stale)).Status);
    }

    /// <summary>A corrupt section can be reset without erasing an unrelated section.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task InvalidSectionIsProtectedButOtherModulesCanSaveAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"input\":{\"schemaVersion\":99,\"values\":{}},\"future\":{\"opaque\":true}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        Assert.Equal(SettingsLoadStatus.UnsupportedVersion, input.LoadResult.Status);
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        Assert.Equal(SettingsSaveStatus.Saved, (await other.SaveAsync(other.BeginEdit())).Status);
        Assert.Equal(99, JsonNode.Parse(store.Data!)!["sections"]!["input"]!["schemaVersion"]!.GetValue<int>());
        Assert.Equal(SettingsSaveStatus.RecoveryRequired, (await input.SaveAsync(input.BeginEdit())).Status);
        Assert.Equal(SettingsSaveStatus.Saved, (await input.ResetAsync(input.Revision)).Status);
        Assert.True(JsonNode.Parse(store.Data!)!["sections"]!["future"]!["opaque"]!.GetValue<bool>());
    }

    /// <summary>Arrays and dictionaries replace defaults while object properties are completed.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task EmptyAndDeletedDictionaryEntriesAreNotRestoredAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"input\":{\"schemaVersion\":1,\"values\":{\"bindings\":{\"game\":{\"jump\":[]}},\"leftStick\":{\"inner\":0}}}}}") };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.Configure<InputSettings>(value => value.Bindings["game"] = new() { ["jump"] = ["key:Space"], ["removed"] = ["key:A"] }));
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        Assert.Empty(input.Current.Value.Bindings["game"]["jump"]);
        Assert.False(input.Current.Value.Bindings["game"].ContainsKey("removed"));
        Assert.Equal(0, input.Current.Value.LeftStick.Inner);
        Assert.Equal(1, input.Current.Value.LeftStick.Outer);
        await input.SaveAsync(input.BeginEdit());
        using ServiceProvider restarted = await CreateProviderAsync(store, services => services.Configure<InputSettings>(value => value.Bindings["game"] = new() { ["removed"] = ["key:A"] }));
        Assert.False(restarted.GetRequiredService<IEditableOptions<InputSettings>>().Current.Value.Bindings["game"].ContainsKey("removed"));
    }

    /// <summary>Candidate capture precedes asynchronous storage and committed cancellation is not failure.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task CandidateIsCapturedBeforeAwaitAndCommitIgnoresLateCancellationAsync()
    {
        var store = new MemoryStore { PauseWrites = true };
        using ServiceProvider provider = await CreateProviderAsync(store);
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        SettingsEdit<InputSettings> edit = input.BeginEdit();
        edit.Value.LeftStick.Inner = 0.2f;
        Task<SettingsSaveResult<InputSettings>> save = input.SaveAsync(edit);
        await store.Entered.Task;
        edit.Value.LeftStick.Inner = 0.8f;
        store.Release.SetResult();
        Assert.Equal(SettingsSaveStatus.Saved, (await save).Status);
        Assert.Equal(0.2f, input.Current.Value.LeftStick.Inner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await input.SaveAsync(input.BeginEdit(), cancellation.Token));
        Assert.Equal(1, input.Revision);
        store.PauseWrites = false;
        store.AfterCommit = cancellation.Cancel;
        using var late = new CancellationTokenSource();
        store.AfterCommit = late.Cancel;
        Assert.Equal(SettingsSaveStatus.Saved, (await input.SaveAsync(input.BeginEdit(), late.Token)).Status);
        Assert.True(late.IsCancellationRequested);
        Assert.Equal(2, input.Revision);
    }

    /// <summary>Input applies one revision at the frame boundary and retains radial direction.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task InputProcessorAppliesRemappingAndDeadZonesAtRefreshAsync()
    {
        using ServiceProvider provider = await CreateProviderAsync(new MemoryStore());
        IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        InputSettings cached = provider.GetRequiredService<IOptions<InputSettings>>().Value;
        var processor = new InputSettingsProcessor(settings);
        SettingsEdit<InputSettings> edit = settings.BeginEdit();
        edit.Value.LeftStick.Inner = 0.2f;
        edit.Value.LeftStick.Outer = 0.8f;
        edit.Value.Bindings["game"] = new() { ["jump"] = ["key:Space"] };
        await settings.SaveAsync(edit);
        Assert.Equal(0, processor.Revision);
        Assert.Empty(processor.GetBindings("game", "jump"));
        Assert.Equal(0.15f, cached.LeftStick.Inner);
        processor.Refresh();
        Assert.Equal(1, processor.Revision);
        Assert.Equal(Vector2.Zero, processor.ApplyStick(ControllerStick.Left, new(0.1f, 0)));
        Assert.Equal(0.5f, processor.ApplyStick(ControllerStick.Left, new(0.5f, 0)).X, precision: 5);
        Vector2 diagonal = processor.ApplyStick(ControllerStick.Left, new(1, 1));
        Assert.Equal(1, diagonal.Length(), precision: 5);
        Assert.Equal(diagonal.X, diagonal.Y);
        Assert.Equal(["key:Space"], processor.GetBindings("game", "jump"));
        Assert.Equal(0, processor.ApplyTrigger(ControllerTrigger.Left, 0));
        Assert.Equal(1, processor.ApplyTrigger(ControllerTrigger.Left, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => processor.ApplyStick(ControllerStick.Left, new(float.NaN, 0)));
    }

    /// <summary>Section IDs and common sources cannot be accidentally assigned twice.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task ConflictingRegistrationsAreRejectedAsync()
    {
        PersistedSettingsSource first = await PersistedSettingsSource.LoadAsync(new MemoryStore());
        PersistedSettingsSource second = await PersistedSettingsSource.LoadAsync(new MemoryStore());
        var services = new ServiceCollection();
        services.AddSettings(first).AddSettings(first).UseInput();
        Assert.Throws<InvalidOperationException>(() => services.AddSettings(second));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<OtherSettings>("input"));
        Assert.Throws<InvalidOperationException>(() => services.AddPersistedOptions<InputSettings>("renamed"));
    }

    /// <summary>Old sections migrate in memory without rewriting on read.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task MigrationIsDeferredAndUnknownPropertiesProtectOnlyTheirModuleAsync()
    {
        var store = new MemoryStore
        {
            Data = Encoding.UTF8.GetBytes("{\"documentVersion\":1,\"sections\":{\"other\":{\"schemaVersion\":1,\"values\":{\"number\":2}},\"input\":{\"schemaVersion\":1,\"values\":{\"typo\":true}}}}"),
        };
        using ServiceProvider provider = await CreateProviderAsync(store, services => services.AddPersistedOptions<OtherSettings>("other").UseJsonDefinition<OtherSettings, OtherDefinition>());
        IEditableOptions<OtherSettings> other = provider.GetRequiredService<IEditableOptions<OtherSettings>>();
        IEditableOptions<InputSettings> input = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        Assert.Equal(12, other.Current.Value.Number);
        Assert.Equal(SettingsLoadStatus.InvalidData, input.LoadResult.Status);
        Assert.Equal(0, store.Writes);
        await other.SaveAsync(other.BeginEdit());
        Assert.Equal(2, JsonNode.Parse(store.Data!)!["sections"]!["other"]!["schemaVersion"]!.GetValue<int>());
        Assert.True(JsonNode.Parse(store.Data!)!["sections"]!["input"]!["values"]!["typo"]!.GetValue<bool>());
    }

    /// <summary>Default configuration, normalization and multiple standard validators run in order.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Fact]
    public async Task StandardOptionsPipelineAndHostlessStartupAreSharedAsync()
    {
        var store = new MemoryStore { Data = Encoding.UTF8.GetBytes(Document("0.3")) };
        using ServiceProvider provider = await CreateProviderAsync(store, services =>
        {
            services.PostConfigure<InputSettings>(value => value.LeftStick.Inner = Math.Clamp(value.LeftStick.Inner, 0, 0.5f));
            services.AddOptions<InputSettings>().Validate(value => value.LeftStick.Inner <= 0.5f, "normalized");
            services.AddOptions<InputSettings>().Validate(value => value.LeftStick.Outer == 1, "outer");
        });
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
        Assert.Equal(0.3f, provider.GetRequiredService<IOptionsMonitor<InputSettings>>().CurrentValue.LeftStick.Inner);
        IEditableOptions<InputSettings> settings = provider.GetRequiredService<IEditableOptions<InputSettings>>();
        SettingsEdit<InputSettings> edit = settings.BeginEdit();
        edit.Value.LeftStick.Inner = 0.8f;
        await settings.SaveAsync(edit);
        Assert.Equal(0.5f, settings.Current.Value.LeftStick.Inner);
        Assert.Equal(0.8f, edit.Value.LeftStick.Inner);
        SettingsEdit<InputSettings> invalid = settings.BeginEdit();
        invalid.Value.LeftStick.Outer = 0.9f;
        Assert.Equal(SettingsSaveStatus.ValidationFailed, (await settings.SaveAsync(invalid)).Status);
    }

    private static ServiceProvider CreateFileProvider(PersistedSettingsSource source) => new ServiceCollection().AddSettings(source).UseInput().BuildServiceProvider();

    private static async Task<ServiceProvider> CreateProviderAsync(MemoryStore store, Action<IServiceCollection>? configure = null)
    {
        PersistedSettingsSource source = await PersistedSettingsSource.LoadAsync(store);
        var configuration = new ConfigurationManager();
        configuration.AddPersistedSettings(source);
        var services = new ServiceCollection();
        services.UseInput();
        configure?.Invoke(services);
        services.AddSettings(source);
        return services.BuildServiceProvider();
    }

    private static string Document(string inner) => "{\"documentVersion\":1,\"sections\":{\"input\":{\"schemaVersion\":1,\"values\":{\"leftStick\":{\"inner\":" + inner + "}}}}}";

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
