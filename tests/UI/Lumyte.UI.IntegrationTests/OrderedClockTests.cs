using Lumyte.Input;
using Lumyte.Input.Abstractions;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.UI.IntegrationTests;

/// <summary>Checks ordered time processing continues after physical empty batches.</summary>
public sealed class OrderedClockTests
{
    /// <summary>Checks both processor paths publish timer output without a physical capture cause.</summary>
    /// <param name="virtualizing">Whether to exercise the virtual generator path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPhysicalBatchStillAdvancesScopedTime(bool virtualizing)
    {
        var raw = new OrderedFixture(InputDeviceKind.Keyboard);
        var step = new ClockStep(virtualizing);
        TimeSpan now = TimeSpan.Zero;
        using IOrderedInputSource source = virtualizing
            ? new OrderedVirtualizingInputSource(raw, (_, _) => step, () => now)
            : new OrderedCorrectingInputSource(raw, (_, _) => new IDeviceDataProcessor[] { step }, () => now);
        using var input = new InputSystem([source]);
        InputWindowId window = new(Guid.NewGuid());
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        raw.Add((0, window, new FocusData(true)));
        input.Update();
        int before = step.ClockCalls;
        records.Clear();
        now = TimeSpan.FromSeconds(1);
        raw.Add();
        input.Update();
        Assert.Equal(before + 1, step.ClockCalls);
        InputRecord output = Assert.Single(records, record => record.Data is KeyData or ControllerButtonData);
        Assert.Null(output.Capture);
        Assert.Equal(window, output.StateWindowId);
        Assert.Equal(2, raw.Acks);
    }

    /// <summary>Checks an empty virtual clock tick cannot fabricate focus after physical focus loss.</summary>
    [Fact]
    public void EmptyClockTickDoesNotRefocusVirtualDevice()
    {
        var raw = new OrderedFixture(InputDeviceKind.Keyboard);
        using var source = new OrderedVirtualizingInputSource(raw, (_, _) => new ClockStep(true), () => TimeSpan.Zero);
        using var input = new InputSystem([source]);
        InputWindowId window = new(Guid.NewGuid());
        raw.Add((0, window, new FocusData(true)));
        input.Update();
        InputDeviceId controller = input.DeviceInfos.Single(device => device.Kind == InputDeviceKind.Controller).Id;
        raw.Add((0, window, new FocusData(false)));
        input.Update();
        Assert.False(input.GetState(controller, window).IsFocused);
        raw.Add();
        input.Update();
        Assert.False(input.GetState(controller, window).IsFocused);
    }

    private sealed class ClockStep(bool virtualizing) : IDeviceDataProcessor, IVirtualDeviceGenerator
    {
        internal int ClockCalls { get; private set; }

        public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
        {
            if (data.Count != 0)
            {
                return virtualizing ? [] : data;
            }

            ClockCalls++;
            return now == TimeSpan.Zero ? [] : [virtualizing ? new ControllerButtonData(ControllerButton.South, true) : new KeyData(Key.A, true, false)];
        }

        public IReadOnlyList<InputData> Generate(InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now) => Process(data, now);

        public void Reset()
        {
        }

        public void Reset(InputDeviceId origin, TimeSpan now)
        {
        }
    }
}
