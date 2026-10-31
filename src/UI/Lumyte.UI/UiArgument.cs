using Lumyte.Composition;
using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>A missing argument, constant, or passive reactive construction value.</summary>
/// <typeparam name="T">The exact declaration member type.</typeparam>
public readonly struct UiArgument<T> : IComposeArgument<T>
{
    private readonly T _constant;

    private UiArgument(T constant, IReadSignal<T>? source)
    {
        _constant = constant;
        Source = source;
        IsSpecified = true;
    }

    /// <inheritdoc/>
    public bool IsSpecified { get; }

    /// <summary>Gets a value indicating whether the argument reads a signal.</summary>
    public bool IsSignal => Source is not null;

    /// <summary>Gets the explicitly supplied constant.</summary>
    public T ConstantValue => IsSpecified && !IsSignal ? _constant : throw new InvalidOperationException("The argument is not a constant.");

    /// <summary>Gets the borrowed source, without subscribing.</summary>
    public IReadSignal<T>? Source { get; }

    /// <summary>Converts a concrete value, including its default, to a specified argument.</summary>
    /// <param name="value">The concrete value.</param>
    public static implicit operator UiArgument<T>(T value) => Constant(value);

    /// <summary>Converts a mutable source to a passive argument.</summary>
    /// <param name="source">The exact typed source.</param>
    public static implicit operator UiArgument<T>(Signal<T> source) => FromSignal(source);

    /// <summary>Converts a derived source to a passive argument.</summary>
    /// <param name="source">The derived value source.</param>
    public static implicit operator UiArgument<T>(Computed<T> source) => FromSignal(source);

    /// <summary>Converts a passive animation projection, retaining its producer identity.</summary>
    /// <param name="source">The exact typed display projection.</param>
    public static implicit operator UiArgument<T>(UiAnimated<T> source) => FromSignal(source);

    /// <summary>Creates an explicitly specified constant.</summary>
    /// <param name="value">The concrete value, including null or default.</param>
    /// <returns>The specified argument.</returns>
    public static UiArgument<T> Constant(T value) => new(value, null);

    /// <summary>Creates an argument for an arbitrary readable source.</summary>
    /// <param name="source">The borrowed reactive value.</param>
    /// <returns>The passive source argument.</returns>
    public static UiArgument<T> FromSignal(IReadSignal<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(default!, source);
    }

    /// <inheritdoc/>
    public T GetInitialValue() => IsSpecified ? Source is { } source ? source.Peek() : _constant : throw new InvalidOperationException("The argument was omitted.");

    /// <inheritdoc/>
    public void AttachTo<TTarget>(TTarget target, object memberKey, T initialValue, Action<TTarget, T> setter)
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(memberKey);
        ArgumentNullException.ThrowIfNull(setter);
        if (target is not Compose.Definitions.Widget widget)
        {
            throw new ArgumentException("UI arguments require widget targets.", nameof(target));
        }

        if (Source is { } source)
        {
            widget.AddSignalArgument(memberKey, () => !EqualityComparer<T>.Default.Equals(source.Value, initialValue));
            if (source is IUiAnimationProjection projection)
            {
                widget.AddAnimationArgument(memberKey, projection, () =>
                {
                    // A function's concrete parameters have already produced its immutable body.
                    // Retain the original comparison value so inherited poses request another build.
                    if (widget is UiFunctionWidget)
                    {
                        return;
                    }

                    T current = source.Peek();
                    setter(target, current);
                    initialValue = current;
                });
            }
        }
    }
}
