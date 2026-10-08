namespace Lumyte.Animation;

/// <summary>Stores typed animation results in reusable slots; consumers own value application.</summary>
public sealed class AnimationOutput
{
    private readonly Dictionary<object, object> _slots = [];
    private long _epoch;

    /// <summary>Clears a typed storage slot without boxing its value.</summary>
    private interface ISlot
    {
        /// <summary>Clears contributions while retaining reusable typed storage.</summary>
        void Clear();
    }

    /// <summary>Clears contributions while retaining reusable typed storage.</summary>
    public void Clear()
    {
        foreach (ISlot slot in _slots.Values)
        {
            slot.Clear();
        }
    }

    /// <summary>Retrieves a computed channel value; returns false when no contribution exists.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The value.</param>
    /// <returns>The operation result.</returns>
    public bool TryGet<T>(AnimationChannel<T> channel, out T value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_slots.TryGetValue(channel, out object? entry) && entry is Slot<T> { HasValue: true } slot)
        {
            value = slot.Value;
            return true;
        }

        value = default!;
        return false;
    }

    internal void BeginEvaluation() => _epoch = checked(_epoch + 1);

    internal void Set<T>(AnimationChannel<T> channel, T value, long priority)
    {
        if (!_slots.TryGetValue(channel, out object? entry))
        {
            entry = new Slot<T>();
            _slots.Add(channel, entry);
        }

        var slot = (Slot<T>)entry;
        if (slot.HasValue && slot.Epoch == _epoch && slot.Priority > priority)
        {
            return;
        }

        slot.Epoch = _epoch;
        slot.Priority = priority;
        slot.Value = value;
        slot.HasValue = true;
    }

    private sealed class Slot<T> : ISlot
    {
        /// <summary>Gets or sets the value.</summary>
        public T Value { get; set; } = default!;

        /// <summary>Gets or sets a value indicating whether has value.</summary>
        public bool HasValue { get; set; }

        internal long Epoch { get; set; }

        internal long Priority { get; set; }

        /// <summary>Clears contributions while retaining reusable typed storage.</summary>
        public void Clear()
        {
            Value = default!;
            HasValue = false;
        }
    }
}
