using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>A passive animation projection whose subscription and frame lifetime belong to its connected surface.</summary>
/// <typeparam name="T">The exact projected value type.</typeparam>
public sealed class UiAnimated<T> : IReadSignal<T>, IUiAnimationProjection
{
    private readonly UiAnimationProducer _producer;
    private readonly Func<T> _detached;
    private readonly Computed<T> _sample;
    private readonly Signal<T> _display;
    private readonly Action<T>? _published;
    private T _staged;

    internal UiAnimated(UiAnimationProducer producer, Func<T> pending, Func<T> detached, Action<T>? published = null)
    {
        _producer = producer;
        _published = published;
        _detached = detached;
        _staged = UiAnimationEvaluation.Read(detached);
        _display = Signal.Create(_staged);
        _sample = Signal.Computed(() =>
        {
            _ = producer.EvaluationEpoch.Value;
            return pending();
        });
    }

    /// <inheritdoc/>
    public T Value => _producer.IsConnected ? _display.Value : UiAnimationEvaluation.Read(_detached);

    /// <inheritdoc/>
    UiAnimationProducer IUiAnimationProjection.Producer => _producer;

    /// <inheritdoc/>
    public T Peek() => _producer.IsConnected ? _display.Peek() : UiAnimationEvaluation.Read(_detached);

    /// <summary>Projects the same producer without transferring its lifetime or reading other signals.</summary>
    /// <typeparam name="TResult">The exact destination property type.</typeparam>
    /// <param name="selector">A pure synchronous transformation of the supplied value.</param>
    /// <returns>A passive projection of the same producer.</returns>
    public UiAnimated<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new(_producer, () => SelectValue(selector, _sample.Peek()), () => SelectValue(selector, Peek()));
    }

    /// <inheritdoc/>
    void IUiAnimationProjection.Stage() => _staged = _sample.Peek();

    /// <inheritdoc/>
    void IUiAnimationProjection.Publish()
    {
        _display.Value = _staged;
        _published?.Invoke(_staged);
    }

    private static TResult SelectValue<TResult>(Func<T, TResult> selector, T value)
    {
        TResult result = Signal.CaptureReads(() => selector(value), out SignalReadSet reads);
        if (!reads.IsEmpty)
        {
            throw new InvalidOperationException("Animation selectors must depend only on their supplied value.");
        }

        return result;
    }
}
