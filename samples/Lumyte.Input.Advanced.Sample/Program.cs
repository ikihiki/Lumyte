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

        // Composition builds defaults once; persisted settings remain the source of runtime overrides.
        Compose.Definitions.Context common = Compose.Context("common")[
            Compose.Context.Actions()[Compose.Action("jump", ActionValueKind.Button)],
            Compose.Context.Bindings()[Compose.Binding(id: "common-jump", actionId: "jump", control: InputControl.ForKey(Key.Space))],
            Compose.Context.Recognitions()[Compose.Recognition(id: "jump-press", kind: RecognitionKind.Press, actions: ["jump"], window: TimeSpan.FromSeconds(1))]];
        Compose.Definitions.Context game = Compose.Context("game", parentId: "common")[
            Compose.Context.Bindings()[Compose.Binding(id: "jump-key", actionId: "jump", control: InputControl.ForKey(Key.J))]];
        ActionProfile defaults = Compose.Profile()[Compose.Profile.Contexts()[common, game]].Build();
        services.Configure<InputActionSettings>(settings =>
        {
            InputActionSettings initial = InputSettingsConverter.ToSettings(defaults);
            settings.Actions = initial.Actions;
            settings.Contexts = initial.Contexts;
            settings.Bindings = initial.Bindings;
            settings.Recognitions = initial.Recognitions;
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
        Console.WriteLine($"jump: {actions.GetState("game", "jump").Value}, buffered: {actions.Buffer.TryConsume("game", "jump-press", input.ElapsedTime, out _)}");
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
