using System.Numerics;
using Lumyte.Input.Processing;
using Xunit;

namespace Lumyte.Input.Advanced.Tests;

/// <summary>Verifies input retention and cleanup when processing operations fail.</summary>
public sealed class ProcessingFailureTests
{
    /// <summary>Verifies failed final acquisition still disconnects and releases both devices.</summary>
    /// <param name="generationFails">Whether generation and reset fail instead of raw acquisition.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisconnectCompletesAfterFinalAcquisitionFailure(bool generationFails)
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var generator = new FailingGenerator();
        using var input = new InputSystem([new VirtualizingInputSource(backend, _ => generator, () => TimeSpan.Zero)]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, new Vector2(100, 0), null));
        input.Update();
        InputDeviceId physicalId = backend.Ids[0];
        InputDeviceId virtualId = input.DeviceInfos.Single(info => info.IdentityKind == InputDeviceIdentityKind.Logical).Id;
        Assert.Equal(Vector2.UnitX, input.GetState(virtualId).GetStick(ControllerStick.Left));

        backend.DisconnectFirst = true;
        device.FailDrain = !generationFails;
        generator.FailGenerate = generationFails;
        generator.FailReset = generationFails;
        AggregateException error = Assert.Throws<AggregateException>(input.Update);

        Assert.Equal(generationFails ? 2 : 1, error.Flatten().InnerExceptions.Count);
        Assert.Empty(input.Devices);
        Assert.False(input.GetState(physicalId).IsConnected);
        Assert.Empty(input.GetState(physicalId).TouchContacts);
        Assert.False(input.GetState(virtualId).IsConnected);
        Assert.Equal(Vector2.Zero, input.GetState(virtualId).GetStick(ControllerStick.Left));
        Assert.Equal(1, device.DisposeCount);
        input.Update();
        Assert.Equal(1, device.DisposeCount);
    }

    /// <summary>Verifies shutdown collects every healthy device despite source or device failures.</summary>
    /// <param name="sourceFails">Whether the inner source fails to shut down.</param>
    /// <param name="generationFails">Whether the first generator fails instead of raw acquisition.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShutdownCollectsRemainingDevicesAfterFailure(bool sourceFails, bool generationFails)
    {
        var first = new TestDevice("first");
        var second = new TestDevice("second");
        var backend = new TestSource(first, second);
        var generator = new FailingGenerator();
        var source = new VirtualizingInputSource(backend, descriptor => descriptor.Name == "first" ? generator : new TouchControllerGenerator(), () => TimeSpan.Zero);
        var input = new InputSystem([source]);
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        input.Update();
        backend.FailShutdown = sourceFails;
        first.FailDrain = !sourceFails && !generationFails;
        generator.FailGenerate = generationFails;
        second.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        second.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, new Vector2(100, 0), null));

        Assert.Throws<AggregateException>(input.Dispose);

        Assert.Equal(2, second.DrainCount);
        Assert.Contains(records, record => record.DeviceId == backend.Ids[1] && record.Data is TouchData { Phase: TouchPhase.Began });
        Assert.Contains(records, record => record.Data is ControllerStickData { Stick: ControllerStick.Left, Value: var value } && value == Vector2.UnitX);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        input.Dispose();
        Assert.Equal(1, second.DisposeCount);
    }

    /// <summary>Verifies a failed acquisition does not starve another device during updates.</summary>
    [Fact]
    public void UpdateCollectsHealthyDevicesAfterAcquisitionFailure()
    {
        var first = new TestDevice("first");
        var second = new TestDevice("second");
        var backend = new TestSource(first, second);
        using var input = new InputSystem([new VirtualizingInputSource(backend, _ => new TouchControllerGenerator(), () => TimeSpan.Zero)]);
        first.FailDrain = true;
        second.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));

        Assert.Throws<AggregateException>(input.Update);

        Assert.Single(input.GetState(backend.Ids[1]).TouchContacts);
        Assert.Equal(1, second.DrainCount);
        first.FailDrain = false;
    }

    /// <summary>Verifies source update failures do not prevent pending input acquisition.</summary>
    [Fact]
    public void UpdateCollectsDevicesAfterInnerSourceFailure()
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device) { FailUpdate = true };
        using var input = new InputSystem([new VirtualizingInputSource(backend, _ => new TouchControllerGenerator(), () => TimeSpan.Zero)]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));

        Assert.Throws<AggregateException>(input.Update);

        Assert.Single(input.GetState(backend.Ids[0]).TouchContacts);
        Assert.Equal(1, device.DrainCount);
    }

    /// <summary>Verifies retry preserves the batch timestamp and resumes after completed stages.</summary>
    [Fact]
    public void CorrectionRetriesFailedStageBeforeCollectingNewInput()
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var first = new ProbeProcessor();
        var second = new ProbeProcessor { FailProcessOnce = true };
        var now = TimeSpan.FromSeconds(1);
        var source = new CorrectingInputSource(backend, _ => [first, second], () => now);
        using var input = new InputSystem([source]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));

        Assert.Throws<AggregateException>(input.Update);
        Assert.Empty(input.GetState(backend.Ids[0]).TouchContacts);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, Vector2.One, null));
        now = TimeSpan.FromSeconds(2);
        input.Update();

        Assert.True(input.GetState(backend.Ids[0]).IsFocused);
        Assert.Equal(Vector2.One, Assert.Single(input.GetState(backend.Ids[0]).TouchContacts).Value.Position);
        Assert.Equal(2, device.DrainCount);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, first.ProcessTimes);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, second.ProcessTimes);
    }

    /// <summary>Verifies retry does not repeat touch tracking or apply a later pipeline prematurely.</summary>
    [Fact]
    public void CorrectionRetriesPreparedContactBoundaryBeforeApplyingNewSettings()
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var source = new CorrectingInputSource(backend, _ => [], () => TimeSpan.Zero);
        using var input = new InputSystem([source]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        input.Update();
        var failing = new ProbeProcessor { FailProcessOnce = true };
        source.SetProcessorFactory(_ => [failing]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Ended, Vector2.Zero, null));
        device.Enqueue(new TouchData(new TouchContactId(2), TouchPhase.Began, Vector2.One, null));
        Assert.Throws<AggregateException>(input.Update);
        var replacement = new ProbeProcessor();
        source.SetProcessorFactory(_ => [replacement]);

        input.Update();

        Assert.Empty(input.GetState(backend.Ids[0]).TouchContacts);
        InputRecord[] records = input.GetRecords(backend.Ids[0]).ToArray();
        Assert.Single(records, record => record.Data is TouchData { Phase: TouchPhase.Began, ContactId.Value: 2 });
        Assert.DoesNotContain(records, record => record.Data is TouchData { Phase: TouchPhase.Ended, ContactId.Value: 1 });
        Assert.Equal(3, device.DrainCount);
        Assert.Single(replacement.ProcessTimes);
    }

    /// <summary>Verifies a failed pipeline reset retains already drained input for retry.</summary>
    [Fact]
    public void CorrectionRetainsBatchWhenReplacingProcessorResetFails()
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var old = new ProbeProcessor();
        var source = new CorrectingInputSource(backend, _ => [old], () => TimeSpan.Zero);
        using var input = new InputSystem([source]);
        input.Update();
        old.FailResetOnce = true;
        source.SetProcessorFactory(_ => []);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));

        Assert.Throws<AggregateException>(input.Update);
        input.Update();

        Assert.Equal(3, device.DrainCount);
        Assert.Single(input.GetState(backend.Ids[0]).TouchContacts);
        Assert.Single(old.ProcessTimes);
    }

    /// <summary>Verifies final collection recovers retained input and input received during failure.</summary>
    /// <param name="disconnect">Whether to remove the device instead of disposing the system.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalCorrectionDrainIncludesInputReceivedAfterFailure(bool disconnect)
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var processor = new ProbeProcessor { FailProcessOnce = true };
        using var input = new InputSystem([new CorrectingInputSource(backend, _ => [processor], () => TimeSpan.Zero)]);
        var records = new List<InputRecord>();
        input.Recorded += records.Add;
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        Assert.Throws<AggregateException>(input.Update);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, Vector2.One, null));

        if (disconnect)
        {
            backend.DisconnectFirst = true;
            input.Update();
        }
        else
        {
            input.Dispose();
        }

        TouchPhase[] phases = records.Select(record => record.Data).OfType<TouchData>().Select(touch => touch.Phase).ToArray();
        Assert.Equal(new[] { TouchPhase.Began, TouchPhase.Moved, TouchPhase.Canceled }, phases);
        Assert.Equal(2, device.DrainCount);
        Assert.Equal(1, device.DisposeCount);
    }

    /// <summary>Verifies recovered output survives a subsequent acquisition or processing failure.</summary>
    /// <param name="processingFails">Whether the newly acquired batch fails in its processor.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorrectionRetainsCompletedOutputWhenNewBatchFails(bool processingFails)
    {
        var device = new TestDevice("touch");
        var backend = new TestSource(device);
        var first = new ProbeProcessor();
        var second = new ProbeProcessor { FailProcessOnce = true };
        using var input = new InputSystem([new CorrectingInputSource(backend, _ => [first, second], () => TimeSpan.Zero)]);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Began, Vector2.Zero, null));
        Assert.Throws<AggregateException>(input.Update);
        device.Enqueue(new TouchData(new TouchContactId(1), TouchPhase.Moved, Vector2.One, null));
        device.FailDrain = !processingFails;
        second.FailOnProcessCall = processingFails ? 3 : 0;

        Assert.Throws<AggregateException>(input.Update);
        Assert.Empty(input.GetState(backend.Ids[0]).TouchContacts);
        device.FailDrain = false;
        input.Update();

        Assert.Equal(Vector2.One, Assert.Single(input.GetState(backend.Ids[0]).TouchContacts).Value.Position);
        TouchData[] touches = input.GetRecords(backend.Ids[0]).ToArray().Select(record => record.Data).OfType<TouchData>().ToArray();
        Assert.Equal(new[] { TouchPhase.Began, TouchPhase.Moved }, touches.Select(touch => touch.Phase));
        Assert.Equal(processingFails ? 3 : 2, first.ProcessTimes.Count);
    }

    /// <summary>Verifies processor cleanup failures do not prevent disposal of the owned device.</summary>
    [Fact]
    public void CorrectedDeviceDisposesInnerDeviceWhenResetFails()
    {
        var device = new TestDevice("touch");
        var processor = new ProbeProcessor { FailResetOnce = true };
        var corrected = new CorrectedInputDevice(device, [processor], () => TimeSpan.Zero);

        Assert.Throws<AggregateException>(corrected.Dispose);

        Assert.Equal(1, device.DisposeCount);
        corrected.Dispose();
        Assert.Equal(1, device.DisposeCount);
    }

    /// <summary>Verifies a later invalid sample cannot partially commit smoothing state.</summary>
    [Fact]
    public void StickSmoothingPreservesStateAfterInvalidBatch()
    {
        var smoothing = new ExponentialStickSmoothing(1);
        smoothing.Process([new ControllerStickData(ControllerStick.Left, Vector2.Zero)], TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => smoothing.Process(
            [new ControllerStickData(ControllerStick.Left, Vector2.UnitX), new ControllerStickData((ControllerStick)2, Vector2.One)],
            TimeSpan.FromSeconds(1)));

        IReadOnlyList<InputData> data = smoothing.Process([new ControllerStickData(ControllerStick.Left, new Vector2(0.5f, 0))], TimeSpan.FromSeconds(1));

        Assert.Equal(0.5f * (1 - MathF.Exp(-1)), Assert.IsType<ControllerStickData>(Assert.Single(data)).Value.X, 5);
    }

    private sealed class TestSource(params TestDevice[] devices) : IInputSource
    {
        private IInputDeviceRegistry? _registry;

        public List<InputDeviceId> Ids { get; } = new();

        public bool DisconnectFirst { get; set; }

        public bool FailUpdate { get; set; }

        public bool FailShutdown { get; set; }

        public void Initialize(IInputDeviceRegistry registry)
        {
            _registry = registry;
            foreach (TestDevice device in devices)
            {
                Ids.Add(registry.RegisterDevice(device));
                device.Enqueue(new FocusData(true));
            }
        }

        public void Update()
        {
            if (DisconnectFirst)
            {
                DisconnectFirst = false;
                _registry!.UnregisterDevice(Ids[0]);
            }

            if (FailUpdate)
            {
                throw new InvalidOperationException("Source update failed.");
            }
        }

        public void Shutdown()
        {
            if (FailShutdown)
            {
                throw new InvalidOperationException("Source shutdown failed.");
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestDevice(string name) : IInputDevice
    {
        private readonly List<InputData> _queue = new();

        public InputDeviceDescriptor Descriptor { get; } = new(InputDeviceKind.Touch, name, InputDeviceIdentityKind.Physical);

        public bool FailDrain { get; set; }

        public int DrainCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Enqueue(InputData data) => _queue.Add(data);

        public IReadOnlyList<InputData> DrainEvents()
        {
            DrainCount++;
            if (FailDrain)
            {
                throw new InvalidOperationException("Device acquisition failed.");
            }

            InputData[] result = _queue.ToArray();
            _queue.Clear();
            return result;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class FailingGenerator : IVirtualDeviceGenerator
    {
        private readonly TouchControllerGenerator _inner = new();

        public bool FailGenerate { get; set; }

        public bool FailReset { get; set; }

        public IReadOnlyList<InputData> Generate(InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now)
        {
            if (FailGenerate)
            {
                throw new InvalidOperationException("Generation failed.");
            }

            return _inner.Generate(origin, data, now);
        }

        public void Reset(InputDeviceId origin, TimeSpan now)
        {
            if (FailReset)
            {
                throw new InvalidOperationException("Generator reset failed.");
            }

            _inner.Reset(origin, now);
        }
    }

    private sealed class ProbeProcessor : IDeviceDataProcessor
    {
        public List<TimeSpan> ProcessTimes { get; } = new();

        public bool FailProcessOnce { get; set; }

        public bool FailResetOnce { get; set; }

        public int FailOnProcessCall { get; set; }

        public IReadOnlyList<InputData> Process(IReadOnlyList<InputData> data, TimeSpan now)
        {
            ProcessTimes.Add(now);
            if (FailProcessOnce || ProcessTimes.Count == FailOnProcessCall)
            {
                FailProcessOnce = false;
                throw new InvalidOperationException("Processing failed.");
            }

            return data;
        }

        public void Reset()
        {
            if (FailResetOnce)
            {
                FailResetOnce = false;
                throw new InvalidOperationException("Processor reset failed.");
            }
        }
    }
}
