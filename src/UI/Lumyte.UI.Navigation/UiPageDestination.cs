namespace Lumyte.UI.Navigation;

/// <summary>Owns an argument snapshot; every read passes through the registered copier.</summary>
public sealed class UiPageDestination
{
    private readonly Func<object?> _copy;

    private UiPageDestination(string route, Type type, Func<object?> copy)
    {
        Route = route;
        ArgumentsType = type;
        _copy = copy;
    }

    /// <summary>Gets the canonical route.</summary>
    public string Route { get; }

    /// <summary>Gets the exact registered type.</summary>
    public Type ArgumentsType { get; }

    /// <summary>Captures arguments once using a pure copier and validator.</summary>
    /// <typeparam name="TArgs">The exact registered type.</typeparam>
    /// <param name="key">The route key.</param>
    /// <param name="arguments">The caller's value.</param>
    /// <param name="captureArguments">A deterministic copier that does not retain resources.</param>
    /// <returns>The immutable destination wrapper.</returns>
    public static UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments, Func<TArgs, TArgs> captureArguments)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(captureArguments);
        TArgs snapshot = captureArguments(arguments);
        return new(key.Value, typeof(TArgs), () => captureArguments(snapshot));
    }

    /// <summary>Copies arguments without exposing the history's owned snapshot.</summary>
    /// <typeparam name="TArgs">The exact registered type.</typeparam>
    /// <returns>A detached copy, or the same immutable value.</returns>
    public TArgs GetArguments<TArgs>()
    {
        if (typeof(TArgs) != ArgumentsType)
        {
            throw new ArgumentException("The argument type must match exactly.");
        }

        return (TArgs)_copy()!;
    }
}
