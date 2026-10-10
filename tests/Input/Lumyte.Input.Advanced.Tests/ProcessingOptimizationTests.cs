using System.Numerics;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies processing snapshots and time advancement across allocation optimizations.</summary>
public sealed class ProcessingOptimizationTests
{
    /// <summary>Verifies later drains and pipeline changes cannot mutate published correction batches.</summary>
    [Fact]
    public void CorrectionSnapshotsSurviveLaterDrainsAndPipelineChanges()
    {
        var device = new QueuedDevice();
        using var corrected = new CorrectedInputDevice(device, [new AnalogCorrection(Vector2.Zero)], () => TimeSpan.Zero);
        device.Enqueue(new FocusData(true), new ControllerStickData(ControllerStick.Left, new Vector2(0.8f, 0.4f)));
        IReadOnlyList<InputData> first = corrected.DrainEvents();
        InputData[] firstSnapshot = first.ToArray();
        device.Enqueue(new ControllerStickData(ControllerStick.Left, new Vector2(-0.4f, 0.7f)));
        IReadOnlyList<InputData> second = corrected.DrainEvents();
        InputData[] secondSnapshot = second.ToArray();
        corrected.SetProcessors([new AnalogCorrection(new Vector2(0.1f, 0.1f))]);
        device.Enqueue(new ControllerStickData(ControllerStick.Left, Vector2.One));

        IReadOnlyList<InputData> changed = corrected.DrainEvents();

        Assert.Equal(firstSnapshot, first);
        Assert.Equal(secondSnapshot, second);
        Assert.False(Assert.IsType<FocusData>(changed[0]).IsFocused);
        Assert.True(Assert.IsType<FocusData>(changed[1]).IsFocused);
    }

    /// <summary>Verifies empty raw batches continue smoothing and preserve earlier results.</summary>
    [Fact]
    public void CorrectedDeviceContinuesSmoothingOnEmptyBatches()
    {
        var device = new QueuedDevice();
        TimeSpan now = TimeSpan.Zero;
        using var corrected = new CorrectedInputDevice(device, [new ExponentialStickSmoothing(1)], () => now);
        device.Enqueue(new FocusData(true), new ControllerStickData(ControllerStick.Left, Vector2.Zero));
        corrected.DrainEvents();
        now = TimeSpan.FromSeconds(1);
        device.Enqueue(new ControllerStickData(ControllerStick.Left, Vector2.UnitX));
        IReadOnlyList<InputData> moving = corrected.DrainEvents();
        now = TimeSpan.FromSeconds(2);

        IReadOnlyList<InputData> continued = corrected.DrainEvents();

        Assert.Equal(1 - MathF.Exp(-1), Assert.IsType<ControllerStickData>(Assert.Single(moving)).Value.X, 5);
        Assert.Equal(1 - MathF.Exp(-2), Assert.IsType<ControllerStickData>(Assert.Single(continued)).Value.X, 5);
        now = TimeSpan.FromSeconds(3);
        corrected.DrainEvents();
        Assert.Equal(1 - MathF.Exp(-2), Assert.IsType<ControllerStickData>(Assert.Single(continued)).Value.X, 5);
    }

    /// <summary>Verifies even an empty settled sample advances the accepted timestamp.</summary>
    [Fact]
    public void SettledSmoothingStillValidatesAndAdvancesTime()
    {
        var smoothing = new ExponentialStickSmoothing(1);
        smoothing.Process([new ControllerStickData(ControllerStick.Left, Vector2.Zero)], TimeSpan.FromSeconds(2));
        Assert.Empty(smoothing.Process([], TimeSpan.FromSeconds(3)));

        Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.Process([], TimeSpan.FromSeconds(2.5)));

        IReadOnlyList<InputData> output = smoothing.Process([new ControllerStickData(ControllerStick.Left, Vector2.UnitX)], TimeSpan.FromSeconds(4));
        Assert.Equal(1 - MathF.Exp(-1), Assert.IsType<ControllerStickData>(Assert.Single(output)).Value.X, 5);
    }

    /// <summary>Verifies empty generation preserves contact baselines and completed snapshots.</summary>
    [Fact]
    public void TouchPairBaselinesAndSnapshotsSurviveEmptyGeneration()
    {
        var generator = new TouchControllerGenerator();
        var origin = new InputDeviceId(1);
        IReadOnlyList<InputData> initial = generator.Generate(
            origin,
            [
                new FocusData(true),
                new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null),
                new TouchData(new TouchContactId(2), TouchPhase.Began, new Vector2(10, 0), null),
            ],
            TimeSpan.Zero);
        InputData[] snapshot = initial.ToArray();
        Assert.Empty(generator.Generate(origin, [], TimeSpan.FromSeconds(1)));

        IReadOnlyList<InputData> moved = generator.Generate(
            origin,
            [new TouchData(new TouchContactId(2), TouchPhase.Moved, new Vector2(0, 110), null)],
            TimeSpan.FromSeconds(2));

        Assert.Equal(snapshot, initial);
        ControllerStickData gesture = Assert.Single(moved.OfType<ControllerStickData>(), item => item.Stick == ControllerStick.Right);
        Assert.Equal(1, gesture.Value.X);
        Assert.Equal(0.5f, gesture.Value.Y, 5);
        generator.Generate(origin, [new TouchData(new TouchContactId(2), TouchPhase.Canceled, Vector2.Zero, null)], TimeSpan.FromSeconds(3));
        Assert.Equal(new Vector2(1, 0.5f), gesture.Value);
    }

    /// <summary>Verifies recovered old and new output remain stable after later batch reuse.</summary>
    [Fact]
    public void RecoveredCorrectionSnapshotSurvivesSubsequentBatchReuse()
    {
        var device = new QueuedDevice();
        var failure = new FailOnceProcessor();
        using var corrected = new CorrectedInputDevice(device, [new AnalogCorrection(Vector2.Zero), failure], () => TimeSpan.Zero);
        device.Enqueue(new FocusData(true), new ControllerStickData(ControllerStick.Left, new Vector2(0.6f, 0)));
        Assert.Throws<InvalidOperationException>(() => corrected.DrainEvents());
        device.Enqueue(new ControllerStickData(ControllerStick.Left, new Vector2(0.9f, 0)));

        IReadOnlyList<InputData> recovered = corrected.DrainEvents();
        InputData[] snapshot = recovered.ToArray();
        device.Enqueue(new ControllerStickData(ControllerStick.Left, Vector2.Zero));
        corrected.DrainEvents();
        corrected.SetProcessors([]);
        corrected.DrainEvents();

        Assert.Equal(snapshot, recovered);
        Assert.Equal(2, recovered.OfType<ControllerStickData>().Count());
        Assert.True(Assert.IsType<FocusData>(recovered[0]).IsFocused);
    }

    /// <summary>Verifies untouched controls and pressure retain their values without changing input snapshots.</summary>
    [Fact]
    public void AnalogCorrectionPreservesUnchangedControlsAndPressure()
    {
        InputData[] input =
        [
            new FocusData(true),
            new ControllerButtonData(ControllerButton.South, true),
            new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.One, null),
            new TouchData(new TouchContactId(2), TouchPhase.Began, Vector2.Zero, 0.5f),
            new PenData(new PenPointerId(1), PenPhase.Down, Vector2.Zero, 1, true, PenButtons.None, false),
        ];
        InputData[] snapshot = input.ToArray();
        var identity = new AnalogCorrection(Vector2.Zero, pressureExponent: 1);
        IReadOnlyList<InputData> unchanged = identity.Process(input, TimeSpan.Zero);
        var curve = new AnalogCorrection(Vector2.Zero, pressureExponent: 2);

        IReadOnlyList<InputData> changed = curve.Process(input, TimeSpan.FromSeconds(1));

        Assert.Equal(snapshot, input);
        Assert.Equal(snapshot, unchanged);
        Assert.Null(Assert.IsType<TouchData>(changed[2]).Pressure);
        Assert.Equal(0.25f, Assert.IsType<TouchData>(changed[3]).Pressure);
        Assert.Equal(1, Assert.IsType<PenData>(changed[4]).Pressure);
        Assert.True(Assert.IsType<ControllerButtonData>(changed[1]).IsDown);
    }

    private sealed class QueuedDevice : IInputDevice
    {
        private readonly Queue<IReadOnlyList<InputData>> _batches = new();

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Controller, "test", InputDeviceIdentityKind.Physical);

        public void Enqueue(params InputData[] data) => _batches.Enqueue(data);

        public IReadOnlyList<InputData> DrainEvents() => _batches.TryDequeue(out IReadOnlyList<InputData>? data) ? data : Array.Empty<InputData>();

        public void Dispose()
        {
        }
    }

    private sealed class FailOnceProcessor : IDeviceDataProcessor
    {
        private bool _fail = true;

        public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
        {
            if (_fail)
            {
                _fail = false;
                throw new InvalidOperationException("The first attempt fails.");
            }

            return data;
        }

        public void Reset()
        {
        }
    }
}
