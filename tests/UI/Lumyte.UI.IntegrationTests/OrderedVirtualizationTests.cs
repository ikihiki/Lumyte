using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks virtual branches share one physical stream and preserve cause metadata.</summary>
public sealed class OrderedVirtualizationTests
{
    /// <summary>Checks one virtual device per physical device, independent window generators and ordered output.</summary>
    [Fact]
    public void VirtualDevicesAreNotDuplicatedPerWindow()
    {
        var raw = new OrderedFixture(InputDeviceKind.Keyboard);
        var created = new List<Generator>();
        IVirtualDeviceGenerator Factory(InputDeviceDescriptor descriptor, InputWindowId? window)
        {
            var generator = new Generator();
            created.Add(generator);
            return generator;
        }

        using var source = new OrderedVirtualizingInputSource(raw, Factory, () => TimeSpan.Zero);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        raw.Add((0, a, new FocusData(true)), (0, b, new FocusData(true)), (0, a, new KeyData(Key.A, true, false)), (0, b, new KeyData(Key.B, true, false)));
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        input.Update();
        Assert.Equal(2, input.Devices.Count);
        Assert.Equal(2, created.Count);
        InputRecord[] virtualKeys = records.Where(record => record.Data is ControllerButtonData).ToArray();
        Assert.Equal(2, virtualKeys.Length);
        Assert.Equal([a, b], virtualKeys.Select(record => record.WindowId));
        Assert.All(virtualKeys, record => Assert.Equal(raw.StreamId, record.Capture!.Value.StreamId));
        Assert.True(virtualKeys[0].Sequence < virtualKeys[1].Sequence);
        Assert.Equal(1, raw.Acks);
        InputDeviceId virtualDevice = virtualKeys[0].DeviceId;
        Assert.True(input.GetState(virtualDevice, a).IsDown(ControllerButton.South));
        Assert.True(input.GetState(virtualDevice, b).IsDown(ControllerButton.South));
        raw.Add((0, b, new KeyData(Key.A, false, false)));
        input.Update();
        Assert.False(input.GetState(virtualDevice, a).IsDown(ControllerButton.South));
        Assert.True(input.GetState(virtualDevice, b).IsDown(ControllerButton.South));
        InputRecord release = input.GetRecords(virtualDevice).Span[^1];
        Assert.Equal(b, release.WindowId);
        Assert.Equal(a, release.StateWindowId);
        raw.Add((0, b, new WindowInputClosedData()));
        input.Update();
        Assert.False(input.GetState(virtualDevice, b).IsDown(ControllerButton.South));
    }

    /// <summary>Checks callback errors occur after acknowledgment and cannot replay a committed batch.</summary>
    [Fact]
    public void NotificationFailureDoesNotReplayCommittedInput()
    {
        var source = new OrderedFixture(InputDeviceKind.Mouse);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        source.Add((0, a, new MouseMoveData(new(1, 2))));
        int moves = 0;
        input.Recorded += record =>
        {
            if (record.Data is MouseMoveData)
            {
                moves++;
                throw new InvalidOperationException("listener");
            }
        };
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(1, source.Acks);
        input.Update();
        Assert.Equal(1, moves);
    }

    /// <summary>Checks crossing a coordinate-space boundary cancels the gesture while physical metadata remains intact.</summary>
    [Fact]
    public void TouchGestureCannotContinueAcrossWindowCoordinateSpaces()
    {
        var raw = new OrderedFixture(InputDeviceKind.Touch);
        using var source = new OrderedVirtualizingInputSource(raw, (_, _) => new TouchControllerGenerator(), () => TimeSpan.Zero);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        var contact = new TouchContactId(1);
        raw.Add((0, a, new TouchData(contact, TouchPhase.Began, new(1, 1), null)), (0, a, new TouchData(contact, TouchPhase.Moved, new(20, 1), null)));
        input.Update();
        InputDeviceId controller = input.DeviceInfos.Single(device => device.Kind == InputDeviceKind.Controller).Id;
        Assert.NotEqual(System.Numerics.Vector2.Zero, input.GetState(controller, a).GetStick(ControllerStick.Left));
        raw.Add((0, b, new TouchData(contact, TouchPhase.Moved, new(500, 500), null)), (0, b, new TouchData(contact, TouchPhase.Ended, new(600, 600), null)));
        input.Update();
        Assert.Equal(System.Numerics.Vector2.Zero, input.GetState(controller, a).GetStick(ControllerStick.Left));
        Assert.DoesNotContain(input.GetRecords(controller).ToArray(), record => record.Data is ControllerButtonData { IsDown: true });
        InputRecord terminal = input.GetRecords(raw.Devices[0]).Span[^1];
        Assert.Equal(b, terminal.WindowId);
        Assert.Equal(a, terminal.StateWindowId);
    }

    private sealed class Generator : IVirtualDeviceGenerator
    {
        public IReadOnlyList<InputData> Generate(InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now) => data.OfType<KeyData>().Select(key => (InputData)new ControllerButtonData(ControllerButton.South, key.IsDown)).ToArray();

        public void Reset(InputDeviceId origin, TimeSpan now)
        {
        }
    }
}
