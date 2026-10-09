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
    private IDeviceDataProcessor[] _processors = processors.ToArray();
    private IDeviceDataProcessor[]? _pending;
    private bool _disposed;
    private bool _focused;

    /// <summary>Gets descriptor.</summary>
    public InputDeviceDescriptor Descriptor => _inner.Descriptor;

    /// <summary>Schedules a replacement pipeline for the next drain.</summary>
    /// <param name = "processors">The processors value.</param>
    public void SetProcessors(IEnumerable<IDeviceDataProcessor> processors) => _pending = processors.ToArray();

    /// <summary>Returns and removes the prepared input batch.</summary>
    /// <returns>The result of the operation.</returns>
    public IReadOnlyList<InputData> DrainEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TimeSpan now = _getTime();
        IReadOnlyList<InputData> data = _inner.DrainEvents();
        if (_pending is not null)
        {
            foreach (IDeviceDataProcessor processor in _processors)
            {
                processor.Reset();
            }

            _blockedTouches.UnionWith(_touches);
            _blockedPens.UnionWith(_pens);
            _touches.Clear();
            _pens.Clear();
            _processors = _pending;
            _pending = null;
            data = (_focused ? new InputData[]
            {
                new FocusData(false),
                new FocusData(true),
            }

            : new InputData[]
            {
                new FocusData(false),
            })

            .Concat(data).ToArray();
        }

        data = data.Where(Track).ToArray();
        foreach (IDeviceDataProcessor processor in _processors)
        {
            if (data.Any(item => item is FocusData { IsFocused: false } or DeviceDisconnectedData))
            {
                processor.Reset();
            }

            data = processor.Process(data, now);
        }

        return data;
    }

    /// <summary>Releases owned device resources.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IDeviceDataProcessor processor in _processors)
        {
            processor.Reset();
        }

        _inner.Dispose();
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
}
