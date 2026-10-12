using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Lumyte.Input;
using Lumyte.Input.Actions;
using Lumyte.Input.Diagnostics;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks the complete Input and Action inspection path used by the diagnostic page.</summary>
public sealed class InputDiagnosticsTests
{
    /// <summary>Checks real system updates produce a detached device and effective mapping snapshot.</summary>
    [Fact]
    public void InputAndActionStateIsPublished()
    {
        using var inspection = new InputInspectionSession();
        var device = new Device();
        var source = new Source(device);
        using var input = new InputSystem([source], inspection);
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.ActivateContext("game");
        device.Add(new FocusData(true));
        device.Add(new KeyData(Key.A, true, false));
        input.Update();
        actions.Advance(input.GetRecords(source.DeviceId), input.ElapsedTime);

        string json = inspection.CreateSnapshot();
        using var snapshot = JsonDocument.Parse(json);
        JsonElement inputSystem = Assert.Single(snapshot.RootElement.GetProperty("inputSystems").EnumerateArray());
        JsonElement publishedDevice = Assert.Single(inputSystem.GetProperty("devices").EnumerateArray());
        Assert.Equal("Keyboard", publishedDevice.GetProperty("kind").GetString());
        Assert.Contains(publishedDevice.GetProperty("state").GetProperty("keys").EnumerateArray(), item => item.GetString() == "A");
        JsonElement actionSystem = Assert.Single(snapshot.RootElement.GetProperty("actionSystems").EnumerateArray());
        JsonElement context = Assert.Single(actionSystem.GetProperty("contexts").EnumerateArray());
        JsonElement action = Assert.Single(context.GetProperty("actions").EnumerateArray());
        Assert.Equal("jump", action.GetProperty("id").GetString());
        Assert.Equal(1, action.GetProperty("value").GetProperty("x").GetSingle());
        Assert.Equal("Started", action.GetProperty("phase").GetString());
        Assert.Equal("Key.A", Assert.Single(action.GetProperty("bindings").EnumerateArray()).GetProperty("control").GetString());
    }

    /// <summary>Checks the generated operation advertises a version-two bounded Stream result.</summary>
    [Fact]
    public void SnapshotOperationUsesStreamTrafficAndDeclaredPayload()
    {
        using var inspection = new InputInspectionSession();
        var set = new DiagnosticOperationSet(new InputDiagnostics(inspection));
        OperationDescriptor operation = Assert.Single(set.Catalog);
        Assert.Equal("get-snapshot", operation.Id);
        Assert.Equal(DiagnosticOperationTraffic.Stream, operation.Traffic);
        DiagnosticField result = Assert.Single(operation.Results);
        Assert.Equal("json", result.Id);
        Assert.Equal(1048576, result.MaxLength);
        var context = new DiagnosticOperationContext(Guid.NewGuid(), Guid.NewGuid(), default, "tester", null, default);
        DiagnosticOperationResult response = set.Invoke(operation.Id, new Dictionary<string, DiagnosticValue>(), context, new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe });
        Assert.Equal("success", response.Status);
        Assert.Equal("[]", JsonDocument.Parse(response.Values!["json"].String!).RootElement.GetProperty("inputSystems").GetRawText());
    }

    private static ActionProfile Profile() => new(
        [new ActionDefinition("jump", ActionValueKind.Button)],
        [new ActionBinding("jump-a", "jump", "game", InputControl.ForKey(Key.A), Vector2.One)],
        [new InputContext("game")],
        ImmutableArray<RecognitionDefinition>.Empty);

    private sealed class Source(Device device) : IInputSource
    {
        public InputDeviceId DeviceId { get; private set; }

        public void Initialize(IInputDeviceRegistry registry) => DeviceId = registry.RegisterDevice(device);

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

    private sealed class Device : IInputDevice
    {
        private readonly List<InputData> _events = [];

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Keyboard, "Test keyboard", InputDeviceIdentityKind.Physical);

        public void Add(InputData data) => _events.Add(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            InputData[] events = _events.ToArray();
            _events.Clear();
            return events;
        }

        public void Dispose()
        {
        }
    }
}
