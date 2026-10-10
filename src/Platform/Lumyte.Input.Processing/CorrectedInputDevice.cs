namespace Lumyte.Input.Processing;

/// <summary>Applies an ordered correction pipeline before input recording.</summary>
/// <remarks>Initializes a new instance of the <see cref = "CorrectedInputDevice"/> class.</remarks>
/// <param name = "inner">The inner value.</param>
/// <param name = "processors">The processors value.</param>
/// <param name = "getTime">The getTime value.</param>
public sealed class CorrectedInputDevice(IInputDevice inner, IEnumerable<IDeviceDataProcessor> processors, Func<TimeSpan> getTime) : IInputDevice
{
    private readonly IInputDevice _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<TimeSpan> _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
    private readonly HashSet<TouchContactId> _touches = new();
    private readonly HashSet<TouchContactId> _blockedTouches = new();
    private readonly HashSet<PenPointerId> _pens = new();
    private readonly HashSet<PenPointerId> _blockedPens = new();
    private readonly List<InputData> _completed = new();
    private IDeviceDataProcessor[] _processors = processors.ToArray();
    private IDeviceDataProcessor[]? _pending;
    private PendingBatch? _batch;
    private bool _disposed;
    private bool _focused;

    /// <summary>Gets descriptor.</summary>
    public InputDeviceDescriptor Descriptor => _inner.Descriptor;

    /// <summary>Schedules a replacement pipeline after any retained batch completes.</summary>
    /// <param name = "processors">The processors value.</param>
    public void SetProcessors(IEnumerable<IDeviceDataProcessor> processors) => _pending = processors.ToArray();

    /// <summary>Returns and removes the prepared input batch.</summary>
    /// <returns>The result of the operation.</returns>
    public IReadOnlyList<InputData> DrainEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool retrying = _batch is not null;
        if (!retrying)
        {
            AcquireBatch();
        }

        CompleteBatch();
        if (retrying)
        {
            // Recovery may be the final drain, so also collect input queued during the failure.
            AcquireBatch();
            CompleteBatch();
        }

        InputData[] result = _completed.ToArray();
        _completed.Clear();
        return result;
    }

    /// <summary>Releases owned device resources.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var errors = new List<Exception>();
        foreach (IDeviceDataProcessor processor in _processors)
        {
            try
            {
                processor.Reset();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        _batch = null;
        _completed.Clear();
        try
        {
            _inner.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Corrected device disposal completed with errors.", errors);
        }
    }

    private void AcquireBatch()
    {
        TimeSpan now = _getTime();
        _batch = new PendingBatch(_inner.DrainEvents(), now, _pending);
        _pending = null;
    }

    private void CompleteBatch()
    {
        PendingBatch batch = _batch!;
        if (!batch.Prepared)
        {
            if (batch.Replacement is not null)
            {
                while (batch.ResetIndex < _processors.Length)
                {
                    _processors[batch.ResetIndex].Reset();
                    batch.ResetIndex++;
                }

                _blockedTouches.UnionWith(_touches);
                _blockedPens.UnionWith(_pens);
                _touches.Clear();
                _pens.Clear();
                _processors = batch.Replacement;
                InputData[] boundary = _focused
                    ? [new FocusData(false), new FocusData(true)]
                    : [new FocusData(false)];
                batch.Data = boundary.Concat(batch.Data).ToArray();
            }

            batch.Data = batch.Data.Where(Track).ToArray();
            batch.Prepared = true;
        }

        while (batch.ProcessorIndex < _processors.Length)
        {
            IDeviceDataProcessor processor = _processors[batch.ProcessorIndex];
            if (!batch.ProcessorReset)
            {
                if (batch.Data.Any(item => item is FocusData { IsFocused: false } or DeviceDisconnectedData))
                {
                    processor.Reset();
                }

                batch.ProcessorReset = true;
            }

            batch.Data = processor.Process(batch.Data, batch.At) ?? throw new InvalidOperationException("A processor returned a null batch.");
            batch.ProcessorIndex++;
            batch.ProcessorReset = false;
        }

        // Keep completed output until every acquisition and processing step in this drain succeeds.
        _completed.AddRange(batch.Data);
        _batch = null;
    }

    private bool Track(InputData data)
    {
        if (data is FocusData focus)
        {
            _focused = focus.IsFocused;
        }

        if (data is TouchData touch)
        {
            if (_blockedTouches.Contains(touch.ContactId))
            {
                if (touch.Phase is TouchPhase.Ended or TouchPhase.Canceled)
                {
                    _blockedTouches.Remove(touch.ContactId);
                }

                return false;
            }

            if (touch.Phase == TouchPhase.Began)
            {
                _touches.Add(touch.ContactId);
            }

            if (touch.Phase is TouchPhase.Ended or TouchPhase.Canceled)
            {
                _touches.Remove(touch.ContactId);
            }
        }

        if (data is PenData pen)
        {
            if (_blockedPens.Contains(pen.PointerId))
            {
                if (pen.Phase is PenPhase.Left or PenPhase.Canceled)
                {
                    _blockedPens.Remove(pen.PointerId);
                }

                return false;
            }

            if (pen.Phase == PenPhase.Entered)
            {
                _pens.Add(pen.PointerId);
            }

            if (pen.Phase is PenPhase.Left or PenPhase.Canceled)
            {
                _pens.Remove(pen.PointerId);
            }
        }

        return true;
    }

    private sealed class PendingBatch(IReadOnlyList<InputData> data, TimeSpan at, IDeviceDataProcessor[]? replacement)
    {
        public IReadOnlyList<InputData> Data { get; set; } = data;

        public TimeSpan At { get; } = at;

        public IDeviceDataProcessor[]? Replacement { get; } = replacement;

        public int ResetIndex { get; set; }

        public int ProcessorIndex { get; set; }

        public bool Prepared { get; set; }

        public bool ProcessorReset { get; set; }
    }
}
