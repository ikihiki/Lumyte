using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks physical ownership, stream order, retry and history without an OS input pump.</summary>
public sealed class OrderedInputTests
{
    /// <summary>Checks interleaved keyboard/mouse records retain acquisition order and origin.</summary>
    [Fact]
    public void DeviceRegistrationOrderDoesNotChangeAcquisitionOrder()
    {
        var source = new OrderedFixture(InputDeviceKind.Mouse, InputDeviceKind.Keyboard);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        source.Add((1, a, new FocusData(true)), (1, b, new FocusData(true)), (1, a, new KeyData(Key.A, true, false)), (0, b, new MouseMoveData(new(4, 5))), (1, b, new KeyData(Key.B, true, false)));
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        input.Update();
        InputRecord[] acquired = records.Where(record => record.Capture is not null).ToArray();
        Assert.Equal(new[] { 1, 1, 1, 0, 1 }.Select(index => source.Devices[index]), acquired.Select(record => record.DeviceId));
        Assert.Equal([a, b, a, b, b], acquired.Select(record => record.WindowId));
        Assert.True(input.GetState(source.Devices[1], a).IsDown(Key.A));
        Assert.False(input.GetState(source.Devices[1], b).IsDown(Key.A));
        Assert.True(input.GetState(source.Devices[0], b).MousePosition == new System.Numerics.Vector2(4, 5));
        Assert.Equal(b, input.GetState(source.Devices[0]).MousePositionWindowId);
        Assert.Equal(1, source.Acks);
    }

    /// <summary>Checks cross-origin terminals and window-local focus cancellation.</summary>
    [Fact]
    public void CrossWindowUpReturnsToOriginalOwnerAndLossKeepsOtherWindows()
    {
        var source = new OrderedFixture(InputDeviceKind.Keyboard);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        InputWindowId b = new(Guid.NewGuid());
        source.Add((0, a, new FocusData(true)), (0, b, new FocusData(true)), (0, a, new KeyData(Key.A, true, false)), (0, b, new KeyData(Key.B, true, false)));
        input.Update();
        source.Add((0, a, new FocusData(false)));
        input.Update();
        Assert.False(input.GetState(source.Devices[0], a).IsDown(Key.A));
        Assert.True(input.GetState(source.Devices[0], b).IsDown(Key.B));
        source.Add((0, b, new KeyData(Key.A, false, false)));
        input.Update();
        InputRecord last = input.GetRecords(source.Devices[0]).Span[^1];
        Assert.Equal(b, last.WindowId);
        Assert.Equal(a, last.StateWindowId);
        Assert.IsType<KeyData>(last.Data);
        Assert.True(input.GetState(source.Devices[0], b).IsDown(Key.B));
    }

    /// <summary>Checks retired IDs are not revived and source stop reports a distinct acquisition gap.</summary>
    [Fact]
    public void RetiredWindowAndStoppedSourceNeverRestart()
    {
        var source = new OrderedFixture(InputDeviceKind.Keyboard);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        source.Add((0, a, new FocusData(true)), (0, a, new WindowInputClosedData()), (0, a, new KeyData(Key.A, true, false)));
        input.Update();
        Assert.False(input.GetState(source.Devices[0], a).IsDown(Key.A));
        ulong cursor = input.ReadRecords(source.Devices[0], 0).NextSequence;
        input.StopSource(source);
        Assert.True(source.Aborted);
        Assert.True(input.ReadRecords(source.Devices[0], cursor).HasAcquisitionGap);
        Assert.False(input.GetState(source.Devices[0]).IsConnected);
        input.StopSource(source);
        input.Update();
    }

    /// <summary>Checks button cancellation retains its type and does not release another button.</summary>
    [Fact]
    public void MouseCancellationIsNotANormalUp()
    {
        var source = new OrderedFixture(InputDeviceKind.Mouse);
        using var input = new InputSystem([source]);
        InputWindowId a = new(Guid.NewGuid());
        source.Add((0, a, new MouseButtonData(MouseButton.Left, true)), (0, a, new MouseButtonData(MouseButton.Right, true)), (0, a, new MouseButtonCanceledData(MouseButton.Left)));
        input.Update();
        Assert.False(input.GetState(source.Devices[0], a).IsDown(MouseButton.Left));
        Assert.True(input.GetState(source.Devices[0], a).IsDown(MouseButton.Right));
        Assert.IsType<MouseButtonCanceledData>(input.GetRecords(source.Devices[0]).Span[^1].Data);
    }

    /// <summary>Checks completed pipeline stages are not replayed when a later stage fails.</summary>
    [Fact]
    public void OrderedCorrectionRetriesWithoutOvertakingOrLosingMetadata()
    {
        var raw = new OrderedFixture(InputDeviceKind.Keyboard, InputDeviceKind.Mouse);
        var first = new Processor();
        var second = new Processor { Fail = true };
        using var corrected = new OrderedCorrectingInputSource(raw, (descriptor, _) => descriptor.Kind == InputDeviceKind.Keyboard ? new[] { first, second } : [], () => TimeSpan.Zero);
        using var input = new InputSystem([corrected]);
        InputWindowId a = new(Guid.NewGuid());
        raw.Add((0, a, new FocusData(true)), (1, a, new MouseMoveData(new(8, 9))));
        Assert.Throws<AggregateException>(input.Update);
        Assert.Equal(0, raw.Acks);
        Assert.Single(input.GetRecords(raw.Devices[1]).ToArray());
        second.Fail = false;
        input.Update();
        Assert.Equal(2, first.Calls);
        Assert.True(second.Calls >= 3);
        Assert.Equal(1, raw.Acks);
        InputRecord record = input.GetRecords(raw.Devices[0]).Span[^1];
        Assert.Equal(a, record.WindowId);
        Assert.Equal(raw.StreamId, record.Capture!.Value.StreamId);
    }

    private sealed class Processor : IDeviceDataProcessor
    {
        internal int Calls { get; private set; }

        internal bool Fail { get; set; }

        public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
        {
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("fixture");
            }

            return data;
        }

        public void Reset()
        {
        }
    }
}
