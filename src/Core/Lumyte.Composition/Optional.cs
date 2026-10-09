namespace Lumyte.Composition;

/// <summary>Distinguishes an omitted argument from an explicitly supplied default or null.</summary>
/// <typeparam name="T">The argument type.</typeparam>
/// <param name="value">The supplied value, including default or null.</param>
public readonly struct Optional<T>(T value)
{
    /// <summary>Gets a value indicating whether the argument was supplied.</summary>
    public bool HasValue { get; } = true;

    /// <summary>Gets the supplied value.</summary>
    /// <exception cref="InvalidOperationException">The argument was omitted.</exception>
    public T Value => HasValue ? value : throw new InvalidOperationException("The optional value was not supplied.");

    /// <summary>Converts a value to a supplied optional argument.</summary>
    /// <param name="value">The supplied value.</param>
    /// <returns>A supplied optional argument.</returns>
    public static implicit operator Optional<T>(T value) => new(value);
}
