using System.Collections.Concurrent;
using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Lumyte.Input.Settings;
using Lumyte.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies input settings against the real persistence service.</summary>
public sealed class SettingsIntegrationTests
{
    /// <summary>Verifies saved rebinds apply on a tick and survive restart.</summary>
    /// <returns>The affine input-thread test.</returns>
    [Fact]
    public Task SavedRebindAppliesAtBoundaryAndSurvivesRestartAsync()
        => RunAffineAsync(SaveRebindAndRestartAsync);

    /// <summary>Verifies invalid saves leave revision, file and runtime unchanged.</summary>
    /// <returns>The asynchronous save operation.</returns>
    [Fact]
    public async Task InvalidSaveLeavesCommittedProfileUnchangedAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            using ServiceProvider provider = CreateProvider(path);
            IEditableOptions<InputActionSettings> options = provider.GetRequiredService<IEditableOptions<InputActionSettings>>();
            SettingsEdit<InputActionSettings> edit = options.BeginEdit();
            edit.Value.Bindings[0].Control = "999";
            long revision = options.Revision;
            SettingsSaveResult<InputActionSettings> result = await options.SaveAsync(edit);
            Assert.Equal(SettingsSaveStatus.ValidationFailed, result.Status);
            Assert.Equal(revision, options.Revision);
            Assert.Equal("Space", options.Current.Value.Bindings[0].Control);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Verifies calibration and gesture dimensions are independently persisted.</summary>
    /// <returns>The asynchronous save operation.</returns>
    [Fact]
    public async Task ProcessingSettingsSaveAndReloadAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            using ServiceProvider provider = CreateProvider(path);
            IEditableOptions<InputProcessingSettings> options = provider.GetRequiredService<IEditableOptions<InputProcessingSettings>>();
            SettingsEdit<InputProcessingSettings> edit = options.BeginEdit();
            edit.Value.DeviceProfiles["Controller"] = new DeviceCorrectionSettings
            {
                DeadZone = 0.3f,
                CenterX = 0.1f,
            };
            edit.Value.TouchRadius = 120;
            Assert.Equal(SettingsSaveStatus.Saved, (await options.SaveAsync(edit)).Status);
            using ServiceProvider restarted = CreateProvider(path);
            InputProcessingSettings restored = restarted.GetRequiredService<IEditableOptions<InputProcessingSettings>>().Current.Value;
            Assert.Equal(120, restored.TouchRadius);
            Assert.Equal(0.3f, restored.DeviceProfiles["Controller"].DeadZone);
            Assert.Equal("Space", restarted.GetRequiredService<IEditableOptions<InputActionSettings>>().Current.Value.Bindings[0].Control);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Verifies a capture cannot overwrite settings committed by another editor.</summary>
    /// <returns>The asynchronous affine test.</returns>
    [Fact]
    public Task RebindRefusesAChangedCommittedRevisionAsync()
        => RunAffineAsync(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                using ServiceProvider provider = CreateProvider(Path.Combine(directory, "settings.json"));
                IEditableOptions<InputActionSettings> options = provider.GetRequiredService<IEditableOptions<InputActionSettings>>();
                var actions = new ActionSystem(InputSettingsConverter.BuildProfile(options.Current.Value));
                var source = new CorrectingInputSource(new EmptySource(), _ => [], () => TimeSpan.Zero);
                var coordinator = new InputSettingsCoordinator(provider.GetRequiredService<IEditableOptions<InputProcessingSettings>>(), options, source, actions);
                coordinator.ApplyCommittedSettings();
                actions.ActivateContext("game");
                actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
                RebindSession session = actions.BeginRebind("jump-key", new RebindOptions(TimeSpan.FromSeconds(1)));
                actions.Advance(new[] { new InputRecord(new InputDeviceId(1), 1, TimeSpan.Zero, new KeyData(Key.J, true, false)) }, TimeSpan.Zero);
                SettingsEdit<InputActionSettings> edit = options.BeginEdit();
                edit.Value.Bindings[0].Control = "K";
                Assert.Equal(SettingsSaveStatus.Saved, (await options.SaveAsync(edit)).Status);
                Assert.Equal(SettingsSaveStatus.Conflict, (await coordinator.SaveRebindAsync(session, RebindConflictPolicy.Allow)).Status);
                Assert.Equal("K", options.Current.Value.Bindings[0].Control);
                Assert.Equal(InputControl.ForKey(Key.Space), actions.ExportProfile().Bindings[0].Control);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        });

    private static async Task SaveRebindAndRestartAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            using ServiceProvider provider = CreateProvider(path);
            IEditableOptions<InputActionSettings> options = provider.GetRequiredService<IEditableOptions<InputActionSettings>>();
            var actions = new ActionSystem(InputSettingsConverter.BuildProfile(options.Current.Value));
            var correction = new CorrectingInputSource(new EmptySource(), _ => [], () => TimeSpan.Zero);
            var coordinator = new InputSettingsCoordinator(provider.GetRequiredService<IEditableOptions<InputProcessingSettings>>(), options, correction, actions);
            coordinator.ApplyCommittedSettings();
            actions.ActivateContext("game");
            actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
            RebindSession session = actions.BeginRebind("jump-key", new RebindOptions(TimeSpan.FromSeconds(5)));
            actions.Advance(new[] { new InputRecord(new InputDeviceId(1), 1, TimeSpan.Zero, new KeyData(Key.J, true, false)) }, TimeSpan.Zero);
            SettingsSaveResult<InputActionSettings> result = await coordinator.SaveRebindAsync(session, RebindConflictPolicy.Reject);
            Assert.Equal(SettingsSaveStatus.Saved, result.Status);
            Assert.Equal(InputControl.ForKey(Key.Space), actions.ExportProfile().Bindings[0].Control);
            coordinator.ApplyCommittedSettings();
            Assert.Equal(InputControl.ForKey(Key.Space), actions.ExportProfile().Bindings[0].Control);
            actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
            Assert.Equal(InputControl.ForKey(Key.J), actions.ExportProfile().Bindings[0].Control);
            Assert.True(session.IsComplete);
            using ServiceProvider restarted = CreateProvider(path);
            InputActionSettings restored = restarted.GetRequiredService<IEditableOptions<InputActionSettings>>().Current.Value;
            Assert.Equal("J", restored.Bindings[0].Control);
            Assert.Equal(InputControl.ForKey(Key.J), InputSettingsConverter.BuildProfile(restored).Bindings[0].Control);
            Assert.DoesNotContain("DeviceId", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static Task RunAffineAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var queue = new BlockingCollection<(SendOrPostCallback Callback, object? State)>();
            SynchronizationContext.SetSynchronizationContext(new InputSynchronizationContext(queue));
            try
            {
                Task execution = test();
                _ = execution.ContinueWith(_ => queue.CompleteAdding(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                foreach ((SendOrPostCallback Callback, object? State) work in queue.GetConsumingEnumerable())
                {
                    work.Callback(work.State);
                }

                execution.GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        { IsBackground = true };
        thread.Start();
        return completion.Task;
    }

    private static ServiceProvider CreateProvider(string path)
    {
        var configuration = new ConfigurationManager();
        var source = new PersistedJsonFileSource(path);
        configuration.AddPersistedJsonFile(source);
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSettings(source);
        services.AddInputSettings();
        services.Configure<InputActionSettings>(settings =>
        {
            settings.Actions.Add(new ActionSettings { Id = "jump" });
            settings.Contexts.Add(new ContextSettings { Id = "game" });
            settings.Bindings.Add(new BindingSettings { Id = "jump-key", ActionId = "jump", ContextId = "game" });
        });
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
        return provider;
    }

    private sealed class InputSynchronizationContext(BlockingCollection<(SendOrPostCallback Callback, object? State)> queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => queue.Add((callback, state));
    }

    private sealed class EmptySource : IInputSource
    {
        public void Initialize(IInputDeviceRegistry registry)
        {
        }

        public void Update()
        {
        }

        public void Shutdown()
        {
        }

        public void Dispose()
        {
        }
    }
}
