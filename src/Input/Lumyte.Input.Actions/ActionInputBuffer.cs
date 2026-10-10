namespace Lumyte.Input.Actions;

/// <summary>Retains completed operations for bounded, single-consumption lookup.</summary>
public sealed class ActionInputBuffer
{
    private readonly InputBufferOptions _options;
    private readonly List<RecognizedAction> _entries = new();
    private TimeSpan _now;

    /// <summary>Initializes a new instance of the <see cref = "ActionInputBuffer"/> class.</summary>
    /// <param name = "options">The options value.</param>
    public ActionInputBuffer(InputBufferOptions options)
    {
        if (options.Lifetime <= TimeSpan.Zero || options.MaxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _options = options;
    }

    /// <summary>Gets count.</summary>
    public int Count => _entries.Count;

    /// <summary>Adds a completed operation and enforces retention limits.</summary>
    /// <param name = "action">The action value.</param>
    /// <param name = "now">The now value.</param>
    public void Add(RecognizedAction action, TimeSpan now)
    {
        Prune(now);
        if (action.At > now)
        {
            throw new ArgumentException("Recognition is from the future.", nameof(action));
        }

        if (now - action.At >= _options.Lifetime)
        {
            return;
        }

        _entries.Add(action);
        if (_entries.Count > _options.MaxEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    /// <summary>Removes and returns the oldest unexpired matching operation.</summary>
    /// <param name = "recognitionId">The recognitionId value.</param>
    /// <param name = "now">The now value.</param>
    /// <param name = "action">The action value.</param>
    /// <returns>The result of the operation.</returns>
    public bool TryConsume(string recognitionId, TimeSpan now, out RecognizedAction? action)
    {
        Prune(now);
        int index = 0;
        while (index < _entries.Count && _entries[index].RecognitionId != recognitionId)
        {
            index++;
        }

        action = index == _entries.Count ? null : _entries[index];
        if (action is null)
        {
            return false;
        }

        _entries.RemoveAt(index);
        return true;
    }

    /// <summary>Removes the oldest matching operation in one context.</summary>
    /// <param name="contextId">The context identifier.</param>
    /// <param name="recognitionId">The recognition identifier.</param>
    /// <param name="now">The monotonic consumption time.</param>
    /// <param name="action">The consumed operation, or null when absent.</param>
    /// <returns>Whether a matching operation was consumed.</returns>
    public bool TryConsume(string contextId, string recognitionId, TimeSpan now, out RecognizedAction? action)
    {
        Prune(now);
        int index = 0;
        while (index < _entries.Count && (_entries[index].ContextId != contextId || _entries[index].RecognitionId != recognitionId))
        {
            index++;
        }

        action = index == _entries.Count ? null : _entries[index];
        if (action is null)
        {
            return false;
        }

        _entries.RemoveAt(index);
        return true;
    }

    /// <summary>Consumes a composed recognition without repeating its identifier.</summary>
    /// <param name="recognition">The recognition definition.</param>
    /// <param name="now">The monotonic consumption time.</param>
    /// <param name="action">The consumed operation, or null when absent.</param>
    /// <returns>Whether an operation was consumed.</returns>
    public bool TryConsume(Compose.Definitions.Recognition recognition, TimeSpan now, out RecognizedAction? action)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        return TryConsume(recognition.Identifier, now, out action);
    }

    /// <summary>Consumes a composed recognition in a composed context.</summary>
    /// <param name="context">The context definition.</param>
    /// <param name="recognition">The recognition definition.</param>
    /// <param name="now">The monotonic consumption time.</param>
    /// <param name="action">The consumed operation, or null when absent.</param>
    /// <returns>Whether a context-scoped operation was consumed.</returns>
    public bool TryConsume(Compose.Definitions.Context context, Compose.Definitions.Recognition recognition, TimeSpan now, out RecognizedAction? action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(recognition);
        return TryConsume(context.Identifier, recognition.Identifier, now, out action);
    }

    /// <summary>Removes expired operations using monotonic time.</summary>
    /// <param name = "now">The now value.</param>
    public void Prune(TimeSpan now)
    {
        if (now < _now)
        {
            throw new ArgumentOutOfRangeException(nameof(now));
        }

        _now = now;
        int retained = 0;
        for (int index = 0; index < _entries.Count; index++)
        {
            RecognizedAction entry = _entries[index];
            if (now - entry.At >= _options.Lifetime)
            {
                continue;
            }

            if (retained != index)
            {
                _entries[retained] = entry;
            }

            retained++;
        }

        if (retained != _entries.Count)
        {
            _entries.RemoveRange(retained, _entries.Count - retained);
        }
    }

    /// <summary>Discards all buffered operations.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Discards operations from the specified context.</summary>
    /// <param name = "contextId">The contextId value.</param>
    public void ClearContext(string contextId) => _entries.RemoveAll(entry => entry.ContextId == contextId);

    /// <summary>Discards operations involving the specified device.</summary>
    /// <param name = "device">The device value.</param>
    public void ClearDevice(InputDeviceId device) => _entries.RemoveAll(entry => entry.Devices.Contains(device));
}
