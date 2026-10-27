using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;
using Lumyte.Input.Diagnostics;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks the complete Input and Action inspection path used by the diagnostic page.</summary>
public sealed class InputDiagnosticsTests
{
    /// <summary>Checks a bounded lease changes the effective Action value and expires on logical time.</summary>
    [Fact]
    public void ActionOverrideIsAppliedAndExpires()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputOverrideResult acquired = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromMilliseconds(10)));

        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        Assert.True(acquired.IsSuccess);
        Assert.Equal(Vector2.UnitX, actions.GetState("game", "jump").Value);

        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromMilliseconds(20));
        Assert.Equal(Vector2.Zero, actions.GetState("game", "jump").Value);
        Assert.Equal("lease-not-found", inspection.GetLease(owner, acquired.Lease!.Id).ErrorCode);
    }

    /// <summary>Checks a device override changes state and history and restores the held physical value when released.</summary>
    [Fact]
    public void DeviceOverridePublishesEffectiveTransitionsAndRestoresPhysicalValue()
    {
        using var inspection = new InputInspectionSession();
        var device = new Device();
        var source = new Source(device);
        using var input = new InputSystem([source], inspection);
        device.Add(new FocusData(true));
        device.Add(new KeyData(Key.A, true, false));
        input.Update();
        Assert.True(input.GetState(source.DeviceId).IsDown(Key.A));

        InputOverrideTarget target = inspection.GetDeviceTarget(input, source.DeviceId, InputControl.ForKey(Key.A));
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputOverrideResult lease = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.Zero, TimeSpan.FromSeconds(1)));
        input.Update();

        Assert.False(input.GetState(source.DeviceId).IsDown(Key.A));
        Assert.IsType<KeyData>(input.GetRecords(source.DeviceId).Span[^1].Data);
        Assert.False(((KeyData)input.GetRecords(source.DeviceId).Span[^1].Data).IsDown);

        Assert.True(inspection.Release(owner, lease.Lease!.Id, lease.Lease.Revision).IsSuccess);
        input.Update();

        Assert.True(input.GetState(source.DeviceId).IsDown(Key.A));
        Assert.True(((KeyData)input.GetRecords(source.DeviceId).Span[^1].Data).IsDown);
    }

    /// <summary>Checks disconnect invalidates a device generation and releases its active lease.</summary>
    [Fact]
    public void DeviceDisconnectInvalidatesTarget()
    {
        using var inspection = new InputInspectionSession();
        var source = new Source(new Device());
        using var input = new InputSystem([source], inspection);
        InputOverrideTarget target = inspection.GetDeviceTarget(input, source.DeviceId, InputControl.ForKey(Key.A));
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputOverrideResult lease = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1)));

        source.Disconnect();
        input.Update();

        Assert.Equal("lease-not-found", inspection.GetLease(owner, lease.Lease!.Id).ErrorCode);
        Assert.Throws<ArgumentException>(() => inspection.GetDeviceTarget(input, source.DeviceId, InputControl.ForKey(Key.A)));
    }

    /// <summary>Checks touch position and pressure targets preserve typed state.</summary>
    [Fact]
    public void TouchTargetsOverridePositionAndPressure()
    {
        using var inspection = new InputInspectionSession();
        var device = new Device(InputDeviceKind.Touch);
        var source = new Source(device);
        using var input = new InputSystem([source], inspection);
        var contact = new TouchContactId(42);
        device.Add(new FocusData(true));
        device.Add(new TouchData(contact, TouchPhase.Began, new(10, 20), 0.25f));
        input.Update();

        InputOverrideTarget position = inspection.GetTouchTarget(input, source.DeviceId, contact);
        InputOverrideTarget pressure = inspection.GetTouchTarget(input, source.DeviceId, contact, pressure: true);
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputLiveSession live = inspection.Begin(owner, [position]);
        inspection.Send(owner, live.Id, 0, [new(position, new(30, 40))]);
        _ = inspection.Acquire(owner, pressure, new(InputOverrideMode.Replace, new(0.75f, 0), TimeSpan.FromSeconds(1)));
        input.Update();

        TouchData state = input.GetState(source.DeviceId).TouchContacts[contact];
        Assert.Equal(new Vector2(30, 40), state.Position);
        Assert.Equal(0.75f, state.Pressure);
        Assert.Equal(TouchPhase.Moved, state.Phase);

        device.Add(new TouchData(contact, TouchPhase.Ended, new(11, 21), 0));
        input.Update();
        Assert.Empty(input.GetState(source.DeviceId).TouchContacts);
        Assert.Throws<ArgumentException>(() => inspection.GetTouchTarget(input, source.DeviceId, contact));
    }

    /// <summary>Checks a mouse position target can inject before the physical device moves.</summary>
    [Fact]
    public void MousePositionTargetInjectsTypedData()
    {
        using var inspection = new InputInspectionSession();
        var source = new Source(new Device(InputDeviceKind.Mouse));
        using var input = new InputSystem([source], inspection);
        InputOverrideTarget target = inspection.GetMouseTarget(input, source.DeviceId);
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        _ = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, new(640, 360), TimeSpan.FromSeconds(1)));
        source.Device.Add(new FocusData(true));
        input.Update();

        Assert.Equal(new Vector2(640, 360), input.GetState(source.DeviceId).MousePosition);
        Assert.IsType<MouseMoveData>(input.GetRecords(source.DeviceId).Span[^1].Data);
    }

    /// <summary>Checks Tape waits on explicit frame boundaries and releases its target atomically.</summary>
    [Fact]
    public void TapeUsesFrameBoundaries()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var tape = new InputTape(
            1,
            [new("jump", ActionValueKind.Button)],
            [new(InputTapeCommandKind.Press, 0, Vector2.Zero), new(InputTapeCommandKind.WaitFrames, 0, Vector2.Zero, 1), new(InputTapeCommandKind.Release, 0, Vector2.Zero)]);
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputTapePlayback playback = inspection.Play(owner, tape, [target], 0);

        inspection.BeginFrame(0);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);
        Assert.Equal(Vector2.UnitX, actions.GetState("game", "jump").Value);
        Assert.True(inspection.GetPlayback(owner, playback.Id).IsRunning);

        inspection.BeginFrame(1);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromMilliseconds(1));
        Assert.Equal(Vector2.Zero, actions.GetState("game", "jump").Value);
        Assert.False(inspection.GetPlayback(owner, playback.Id).IsRunning);
    }

    /// <summary>Checks ordered live batches replace values and reject replay.</summary>
    [Fact]
    public void LiveInputRejectsDuplicateBatch()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputLiveSession live = inspection.Begin(owner, [target]);
        inspection.Send(owner, live.Id, 1, [new(target, Vector2.UnitX)]);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);

        Assert.Equal(Vector2.UnitX, actions.GetState("game", "jump").Value);
        Assert.Throws<InvalidOperationException>(() => inspection.Send(owner, live.Id, 1, [new(target, Vector2.Zero)]));
        inspection.Stop(owner, live.Id);
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.FromMilliseconds(1));
        Assert.Equal(Vector2.Zero, actions.GetState("game", "jump").Value);
    }

    /// <summary>Checks targets are exclusive and a profile generation invalidates active control.</summary>
    [Fact]
    public void ProfileChangeInvalidatesExclusiveTarget()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputOverrideResult lease = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1)));

        Assert.Equal("target-busy", inspection.Acquire(new(Guid.NewGuid(), "other"), target, new(InputOverrideMode.Replace, Vector2.Zero, TimeSpan.FromSeconds(1))).ErrorCode);
        actions.QueueProfile(Profile());
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);

        Assert.Equal("lease-not-found", inspection.GetLease(owner, lease.Lease!.Id).ErrorCode);
        Assert.Throws<ArgumentException>(() => inspection.Begin(owner, [target]));
        Assert.NotEqual(target, inspection.GetActionTarget(actions, "game", "jump"));
    }

    /// <summary>Checks real system updates produce a detached device and effective mapping snapshot.</summary>
    [Fact]
    public void InputAndActionStateIsPublished()
    {
        using var inspection = new InputInspectionSession();
        var device = new Device();
        var source = new Source(device);
        using var input = new InputSystem([source], inspection);
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
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
        Assert.NotEqual(Guid.Empty, publishedDevice.GetProperty("targets").GetProperty("Key.A").GetGuid());
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
        OperationDescriptor operation = Assert.Single(set.Catalog, item => item.Id == "get-snapshot");
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

    /// <summary>Checks the generated Control operation authenticates ownership and changes Action evaluation.</summary>
    [Fact]
    public void GeneratedOverrideOperationControlsAction()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.QueueContextActivation("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var set = new DiagnosticOperationSet(new InputDiagnostics(inspection));
        OperationDescriptor operation = Assert.Single(set.Catalog, item => item.Id == "override-acquire");
        Assert.Equal(DiagnosticOperationTraffic.Control, operation.Traffic);
        var session = Guid.NewGuid();
        var context = new DiagnosticOperationContext(Guid.NewGuid(), session, new(0, 0), "operator", null, default);
        var arguments = new Dictionary<string, DiagnosticValue>
        {
            ["target-id"] = DiagnosticValue.From(target.Id.ToString("D")),
            ["mode"] = DiagnosticValue.From("replace"),
            ["x"] = DiagnosticValue.From(1d),
            ["y"] = DiagnosticValue.From(0d),
            ["duration-ms"] = DiagnosticValue.From(1000L),
        };

        DiagnosticOperationResult response = set.Invoke(operation.Id, arguments, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.OverrideInput });
        actions.Advance(ReadOnlyMemory<InputRecord>.Empty, TimeSpan.Zero);

        Assert.Equal("success", response.Status);
        Assert.Equal(Vector2.UnitX, actions.GetState("game", "jump").Value);
        Assert.NotEqual(Guid.Empty, Guid.Parse(response.Values!["lease-id"].String!));
    }

    private static ActionProfile Profile() => new(
        [new ActionDefinition("jump", ActionValueKind.Button)],
        [new ActionBinding("jump-a", "jump", "game", InputControl.ForKey(Key.A), Vector2.One)],
        [new InputContext("game")],
        ImmutableArray<RecognitionDefinition>.Empty);

    private sealed class Source(Device device) : IInputSource
    {
        private IInputDeviceRegistry? _registry;
        private bool _disconnect;

        public InputDeviceId DeviceId { get; private set; }

        public Device Device => device;

        public void Initialize(IInputDeviceRegistry registry)
        {
            _registry = registry;
            DeviceId = registry.RegisterDevice(device);
        }

        public void Disconnect() => _disconnect = true;

        public void Update()
        {
            if (_disconnect)
            {
                _registry!.UnregisterDevice(DeviceId);
                _disconnect = false;
            }
        }

        public void Shutdown()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Device(InputDeviceKind kind = InputDeviceKind.Keyboard) : IInputDevice
    {
        private readonly List<InputData> _events = [];

        public InputDeviceDescriptor Descriptor { get; } = new(kind, "Test device", InputDeviceIdentityKind.Physical);

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
