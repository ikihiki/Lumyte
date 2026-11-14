using System.Numerics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Lumyte.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Input.Settings.Benchmark;

/// <summary>Measures conversion, generated JSON and real memory-backed settings commits.</summary>
[BenchmarkCategory("Input", "Settings", "Screening", "Comparison", "Diagnostic")]
public class SettingsBenchmarks
{
    private ServiceProvider _provider = null!;
    private IEditableOptions<InputActionSettings> _options = null!;
    private InputSettingsCoordinator _coordinator = null!;
    private CorrectingInputSource _source = null!;
    private ActionSystem _system = null!;
    private InputActionSettings _settings = null!;
    private InputActionSettings _invalid = null!;
    private ActionProfile _profile = null!;
    private byte[] _json = null!;
    private long _tick;

    /// <summary>Gets the control still applied after committing but before queueing.</summary>
    public int PendingControl { get; private set; }

    /// <summary>Gets the control still applied after queueing but before advancing.</summary>
    public int QueuedControl { get; private set; }

    /// <summary>Gets the applied control token index.</summary>
    public int AppliedControl => _system.ExportProfile().Bindings[0].Control.Index;

    /// <summary>Gets a detached committed snapshot.</summary>
    public SettingsSnapshot<InputActionSettings> Committed => _options.Current;

    /// <summary>Creates the finite inherited settings document.</summary>
    /// <returns>The detached settings.</returns>
    public static InputActionSettings CreateSettings() => InputSettingsConverter.ToSettings(new ActionProfile(
        [new("jump", ActionValueKind.Button), new("move", ActionValueKind.Axis2D)],
        [new("jump-key", "jump", "common", InputControl.ForKey(Key.Space), Vector2.UnitX), new("stick", "move", "game", new(InputControlKind.ControllerStick, (int)ControllerStick.Left), Vector2.One)],
        [new("common"), new("game", ParentId: "common", Actions: [new("move", ActionValueKind.Axis2D, Sensitivity: 0.75f)])],
        [new("press", "game", RecognitionKind.Press, ["jump"], TimeSpan.FromSeconds(0.1))]));

    /// <summary>Creates a real settings service using an isolated memory store.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _tick = 0;
        _settings = CreateSettings();
        _profile = InputSettingsConverter.BuildProfile(_settings);
        _json = JsonSerializer.SerializeToUtf8Bytes(_settings, InputSettingsJsonContext.Default.InputActionSettings);
        _invalid = JsonSerializer.Deserialize(_json, InputSettingsJsonContext.Default.InputActionSettings)!;
        _invalid.Bindings[0].Control = "999";
        _provider = CreateProvider();
        _options = _provider.GetRequiredService<IEditableOptions<InputActionSettings>>();
        _system = new ActionSystem(_profile);
        _source = new CorrectingInputSource(new EmptySource(), _ => [], () => TimeSpan.Zero);
        _coordinator = new InputSettingsCoordinator(_provider.GetRequiredService<IEditableOptions<InputProcessingSettings>>(), _options, _source, _system);
        _coordinator.QueueCommittedSettings();
        _system.QueueContextActivation("game");
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
    }

    /// <summary>Releases the isolated owner and provider.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _system.Dispose();
        _source.Dispose();
        _provider.Dispose();
    }

    /// <summary>Converts stable tokens and validates the runtime profile.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public ActionProfile ConvertToProfile() => InputSettingsConverter.BuildProfile(_settings);

    /// <summary>Exports immutable definitions to detached settings.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public InputActionSettings ToSettings() => InputSettingsConverter.ToSettings(_profile);

    /// <summary>Validates a valid settings candidate.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public bool Validate() => InputSettingsConverter.ValidateActions(_settings);

    /// <summary>Rejects an invalid stable control token.</summary>
    /// <returns>Whether the invalid candidate was accepted.</returns>
    [Benchmark]
    public bool ValidateInvalid() => InputSettingsConverter.ValidateActions(_invalid);

    /// <summary>Serializes using generated metadata.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(_settings, InputSettingsJsonContext.Default.InputActionSettings);

    /// <summary>Deserializes and validates using generated metadata.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public ActionProfile Deserialize() => InputSettingsConverter.BuildProfile(JsonSerializer.Deserialize(_json, InputSettingsJsonContext.Default.InputActionSettings)!);

    /// <summary>Constructs, queues and applies the initial committed revision.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public int ColdCoordinator()
    {
        var workload = new SettingsBenchmarks();
        workload.Setup();
        try
        {
            return workload.AppliedControl;
        }
        finally
        {
            workload.Cleanup();
        }
    }

    /// <summary>Commits a changing candidate through real settings then queues and applies it.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public long CommitQueueApply()
    {
        SettingsEdit<InputActionSettings> edit = _options.BeginEdit();
        edit.Value.Bindings[0].Control = edit.Value.Bindings[0].Control == "J" ? "K" : "J";
        SettingsSaveResult<InputActionSettings> saved = _options.SaveAsync(edit).GetAwaiter().GetResult();
        if (saved.Status != SettingsSaveStatus.Saved)
        {
            throw new InvalidOperationException(saved.Status.ToString());
        }

        PendingControl = AppliedControl;
        _coordinator.QueueCommittedSettings();
        QueuedControl = AppliedControl;
        int pending = QueuedControl;
        _source.Update();
        _system.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromMilliseconds(++_tick));
        return saved.Snapshot.Revision + pending + AppliedControl;
    }

    /// <summary>Polls an unchanged revision without scheduling a new profile.</summary>
    /// <returns>The measured result.</returns>
    [Benchmark]
    public long PollCommitted()
    {
        _coordinator.QueueCommittedSettings();
        return _options.Revision;
    }

    private static ServiceProvider CreateProvider()
    {
        PersistedSettingsSource source = PersistedSettingsSource.LoadAsync(new MemoryStore()).GetAwaiter().GetResult();
        IConfigurationRoot configuration = new ConfigurationBuilder().AddPersistedSettings(source).Build();
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(_ => (IDisposable)configuration);
        services.AddInputSettings();
        services.Configure<InputActionSettings>(settings =>
        {
            InputActionSettings initial = CreateSettings();
            settings.Actions = initial.Actions;
            settings.Bindings = initial.Bindings;
            settings.Contexts = initial.Contexts;
            settings.Recognitions = initial.Recognitions;
        });
        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
        return provider;
    }

    private sealed class MemoryStore : ISettingsStore
    {
        private byte[]? _data;

        public ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_data?.ToArray());

        public ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _data = data.ToArray();
            return ValueTask.CompletedTask;
        }
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
