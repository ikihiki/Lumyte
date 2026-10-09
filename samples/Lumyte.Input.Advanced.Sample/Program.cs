using Lumyte.Input.Actions;
using Lumyte.Input.Processing;
using Lumyte.Input.Settings;
using Lumyte.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.Input.Advanced.Sample;

internal static class Program
{
    private static void Main(string[] args)
    {
        string path = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "lumyte-input-sample.json"));
        var source = new PersistedJsonFileSource(path);
        using var configuration = new ConfigurationManager();
        configuration.AddPersistedJsonFile(source);
        var services = new ServiceCollection();
        services.AddSettings(source);
        services.AddInputSettings();
        services.Configure<InputActionSettings>(settings =>
        {
            settings.Actions.Add(new ActionSettings { Id = "jump" });
            settings.Contexts.Add(new ContextSettings { Id = "game" });
            settings.Bindings.Add(new BindingSettings { Id = "jump-key", ActionId = "jump", ContextId = "game", Control = "Space" });
            settings.Recognitions.Add(new RecognitionSettings { Id = "jump-press", ContextId = "game", Actions = ["jump"] });
        });
        services.AddSingleton<DemoSource>();
        services.AddSingleton<InputTimeSource>();
        services.AddSingleton(provider => new CorrectingInputSource(provider.GetRequiredService<DemoSource>(), InputSettingsConverter.BuildCorrections(provider.GetRequiredService<IEditableOptions<InputProcessingSettings>>().Current.Value), provider.GetRequiredService<InputTimeSource>().GetElapsedTime));
        services.AddSingleton(provider => new VirtualizingInputSource(provider.GetRequiredService<CorrectingInputSource>(), descriptor => descriptor.Kind == InputDeviceKind.Touch ? new TouchControllerGenerator() : null, provider.GetRequiredService<InputTimeSource>().GetElapsedTime));
        services.AddSingleton<IInputSource>(provider => provider.GetRequiredService<VirtualizingInputSource>());
        services.AddSingleton(provider =>
        {
            var system = new InputSystem(provider.GetServices<IInputSource>());
            provider.GetRequiredService<InputTimeSource>().Attach(system);
            return system;
        });
        services.AddSingleton(provider =>
        {
            InputActionSettings settings = provider.GetRequiredService<IEditableOptions<InputActionSettings>>().Current.Value;
            return new ActionSystem(InputSettingsConverter.BuildProfile(settings), new InputBufferOptions(TimeSpan.FromSeconds(settings.BufferLifetimeSeconds), settings.BufferMaxEntries));
        });
        services.AddSingleton<InputSettingsCoordinator>();
        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISettingsDocument>().ValidateRegisteredSettings();
        InputSystem input = provider.GetRequiredService<InputSystem>();
        ActionSystem actions = provider.GetRequiredService<ActionSystem>();
        InputSettingsCoordinator coordinator = provider.GetRequiredService<InputSettingsCoordinator>();
        DemoSource backend = provider.GetRequiredService<DemoSource>();
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        actions.Recognized += operation => Console.WriteLine($"recognized: {operation.RecognitionId}");
        coordinator.ApplyCommittedSettings();
        actions.ActivateContext("game");
        Tick();
        RebindSession capture = actions.BeginRebind("jump-key", new RebindOptions(TimeSpan.FromSeconds(5)));
        backend.Send(new KeyData(Key.J, true, false));
        Tick();
        if (capture.Candidate is null)
        {
            throw new InvalidOperationException("The demo key was not captured.");
        }

        // Blocking is confined to this console demo; a UI awaits saving and applies on its input thread.
        SettingsSaveResult<InputActionSettings> saved = coordinator.SaveRebindAsync(capture, RebindConflictPolicy.Allow).GetAwaiter().GetResult();
        Console.WriteLine($"save: {saved.Status}, file: {path}");
        coordinator.ApplyCommittedSettings();
        Tick();
        backend.Send(new KeyData(Key.J, false, false));
        Tick();
        backend.Send(new KeyData(Key.J, true, false));
        Tick();
        Console.WriteLine($"jump: {actions.GetState("jump").Value}, buffered: {actions.Buffer.TryConsume("jump-press", input.ElapsedTime, out _)}");
        void Tick()
        {
            records.Clear();
            input.Update();
            actions.Advance(records.ToArray(), input.ElapsedTime);
            foreach (InputDeviceInfo info in input.DeviceInfos)
            {
                input.SetRetentionPolicy(info.Id, new InputRetentionPolicy(MaxRecords: 256));
            }
        }
    }

    private sealed class DemoSource : IInputSource
    {
        private readonly DemoDevice _device = new();

        public void Send(InputData data) => _device.Send(data);

        public void Initialize(IInputDeviceRegistry registry)
        {
            registry.RegisterDevice(_device);
            _device.Send(new FocusData(true));
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

    private sealed class DemoDevice : IInputDevice
    {
        private readonly List<InputData> _queue = new();

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "demo keyboard", InputDeviceIdentityKind.Physical);

        public void Send(InputData data) => _queue.Add(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            InputData[] result = _queue.ToArray();
            _queue.Clear();
            return result;
        }

        public void Dispose()
        {
        }
    }
}
