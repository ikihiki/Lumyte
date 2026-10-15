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
        actions.ActivateContext("game");
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

    /// <summary>Checks Tape waits on explicit frame boundaries and releases its target atomically.</summary>
    [Fact]
    public void TapeUsesFrameBoundaries()
    {
        using var inspection = new InputInspectionSession();
        using var actions = new ActionSystem(Profile(), bufferOptions: null, inspection);
        actions.ActivateContext("game");
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
        actions.ActivateContext("game");
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
        actions.ActivateContext("game");
        InputOverrideTarget target = inspection.GetActionTarget(actions, "game", "jump");
        var owner = new InputOverrideOwner(Guid.NewGuid(), "tester");
        InputOverrideResult lease = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1)));

        Assert.Equal("target-busy", inspection.Acquire(new(Guid.NewGuid(), "other"), target, new(InputOverrideMode.Replace, Vector2.Zero, TimeSpan.FromSeconds(1))).ErrorCode);
        actions.ApplyProfile(Profile());
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
        actions.ActivateContext("game");
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
