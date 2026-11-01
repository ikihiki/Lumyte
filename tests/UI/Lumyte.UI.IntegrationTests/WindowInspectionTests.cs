using System.Numerics;
using System.Text.Json;
using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Actions;
using Lumyte.Input.Diagnostics;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks overrides use physical owner metadata and explicitly selected windows.</summary>
public sealed class WindowInspectionTests
{
    /// <summary>Checks a scope override cannot affect another window and never extends a physical owner.</summary>
    [Fact]
    public void WindowOverrideAndPhysicalOwnershipRemainIndependent()
    {
        using var inspection = new InputInspectionSession();
        var source = new OrderedFixture(InputDeviceKind.Keyboard);
        using var input = new InputSystem([source], inspection);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        source.Add((0, a, new FocusData(true)), (0, b, new FocusData(true)), (0, a, new KeyData(Key.A, true, false)));
        input.Update();
        InputOverrideTarget target = inspection.GetWindowTarget(input, source.Devices[0], a, InputControl.ForKey(Key.A));
        var owner = new InputOverrideOwner(Guid.NewGuid(), "test");
        InputOverrideResult lease = inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1)));
        Assert.True(lease.IsSuccess);
        source.Add((0, b, new KeyData(Key.A, false, false)), (0, b, new KeyData(Key.A, true, false)));
        input.Update();
        Assert.True(input.GetState(source.Devices[0], a).IsDown(Key.A));
        Assert.True(input.GetState(source.Devices[0], b).IsDown(Key.A));
        InputRecord[] keys = input.GetRecords(source.Devices[0]).ToArray().Where(record => record.Data is KeyData).ToArray();
        Assert.Equal(a, keys[^2].StateWindowId);
        Assert.Equal(b, keys[^1].StateWindowId);
        source.Add((0, a, new FocusData(false)));
        input.Update();
        Assert.False(input.GetState(source.Devices[0], a).IsDown(Key.A));
        Assert.True(input.GetState(source.Devices[0], b).IsDown(Key.A));
        Assert.Equal("lease-not-found", inspection.GetLease(owner, lease.Lease!.Id).ErrorCode);
        using var snapshot = JsonDocument.Parse(inspection.CreateSnapshot());
        Assert.Equal(2, snapshot.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(2, snapshot.RootElement.GetProperty("inputSystems")[0].GetProperty("windowScopes").GetArrayLength());
    }

    /// <summary>Checks empty acquisition boundaries deliver a synthetic scoped override without inventing a capture stamp.</summary>
    [Fact]
    public void EmptyBatchAdvancesWindowOverrideAndClosedScopeRejectsTargets()
    {
        using var inspection = new InputInspectionSession();
        var source = new OrderedFixture(InputDeviceKind.Keyboard);
        using var input = new InputSystem([source], inspection);
        InputWindowId a = new(Guid.NewGuid());
        source.Add((0, a, new FocusData(true)));
        input.Update();
        InputOverrideTarget target = inspection.GetWindowTarget(input, source.Devices[0], a, InputControl.ForKey(Key.A));
        var owner = new InputOverrideOwner(Guid.NewGuid(), "test");
        Assert.True(inspection.Acquire(owner, target, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1))).IsSuccess);
        source.Add();
        input.Update();
        InputRecord last = input.GetRecords(source.Devices[0]).Span[^1];
        Assert.True(input.GetState(source.Devices[0], a).IsDown(Key.A));
        Assert.Null(last.Capture);
        Assert.Equal(a, last.StateWindowId);
        source.Add((0, a, new WindowInputClosedData()));
        input.Update();
        Assert.Throws<KeyNotFoundException>(() => inspection.GetWindowTarget(input, source.Devices[0], a, InputControl.ForKey(Key.A)));
    }

    /// <summary>Checks typed position/pressure overrides keep their window and end with the contact.</summary>
    [Fact]
    public void TouchOverridesAreScopedAndTerminalRecordsKeepTheirType()
    {
        using var inspection = new InputInspectionSession();
        var source = new OrderedFixture(InputDeviceKind.Touch);
        using var input = new InputSystem([source], inspection);
        InputWindowId a = new(Guid.NewGuid());
        var contact = new TouchContactId(1);
        source.Add((0, a, new TouchData(contact, TouchPhase.Began, new(1, 2), 0.2f)));
        input.Update();
        InputOverrideTarget position = inspection.GetTouchTarget(input, source.Devices[0], a, contact);
        InputOverrideTarget pressure = inspection.GetTouchTarget(input, source.Devices[0], a, contact, pressure: true);
        var owner = new InputOverrideOwner(Guid.NewGuid(), "test");
        Assert.True(inspection.Acquire(owner, position, new(InputOverrideMode.Replace, new(50, 60), TimeSpan.FromSeconds(1))).IsSuccess);
        Assert.True(inspection.Acquire(owner, pressure, new(InputOverrideMode.Replace, new(0.7f, 0), TimeSpan.FromSeconds(1))).IsSuccess);
        source.Add();
        input.Update();
        InputDeviceState state = input.GetState(source.Devices[0], a);
        Assert.Equal(new Vector2(50, 60), state.TouchContacts[contact].Position);
        Assert.Equal(0.7f, state.TouchContacts[contact].Pressure);
        Assert.Equal(a, state.TouchPositionWindows[contact]);
        Assert.Null(input.GetRecords(source.Devices[0]).Span[^1].Capture);
        source.Add((0, a, new TouchData(contact, TouchPhase.Ended, new(3, 4), 0)));
        input.Update();
        Assert.Empty(input.GetState(source.Devices[0], a).TouchContacts);
        Assert.Equal(TouchPhase.Ended, Assert.IsType<TouchData>(input.GetRecords(source.Devices[0]).Span[^1].Data).Phase);
        Assert.Throws<KeyNotFoundException>(() => inspection.GetTouchTarget(input, source.Devices[0], a, contact));
    }

    /// <summary>Checks the whole-device target cannot silently inject into one of several windows.</summary>
    [Fact]
    public void AggregateTargetsRequireExplicitWindowSelection()
    {
        using var inspection = new InputInspectionSession();
        var source = new OrderedFixture(InputDeviceKind.Mouse);
        using var input = new InputSystem([source], inspection);
        InputOverrideTarget aggregate = inspection.GetDeviceTarget(input, source.Devices[0], new(InputControlKind.MouseButton, (int)MouseButton.Left));
        InputWindowId a = new(Guid.NewGuid());
        source.Add((0, a, new MouseMoveData(new(1, 2))));
        input.Update();
        var owner = new InputOverrideOwner(Guid.NewGuid(), "test");
        Assert.Equal("window-required", inspection.Acquire(owner, aggregate, new(InputOverrideMode.Replace, Vector2.UnitX, TimeSpan.FromSeconds(1))).ErrorCode);
        InputOverrideTarget position = inspection.GetMouseTarget(input, source.Devices[0], a);
        Assert.True(inspection.Acquire(owner, position, new(InputOverrideMode.Replace, new(70, 80), TimeSpan.FromSeconds(1))).IsSuccess);
        source.Add();
        input.Update();
        Assert.Equal(new Vector2(70, 80), input.GetState(source.Devices[0], a).MousePosition);
        input.StopSource(source);
        Assert.Throws<KeyNotFoundException>(() => inspection.GetMouseTarget(input, source.Devices[0], a));
    }
}
