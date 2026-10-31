namespace Lumyte.Signals;

/// <summary>A readable value whose synchronous reads can be tracked.</summary>
/// <typeparam name="T">The value type.</typeparam>
public interface IReadSignal<out T>
{
    /// <summary>Gets the current value and records the read.</summary>
    T Value { get; }

    /// <summary>Reads without subscribing the surrounding evaluation.</summary>
    /// <returns>The current value.</returns>
    T Peek();
}
