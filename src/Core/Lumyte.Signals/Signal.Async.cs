namespace Lumyte.Signals;

/// <summary>Creates independent reactive values and automatically delivered observers.</summary>
public static partial class Signal
{
    /// <summary>Starts an asynchronous factory and exposes its latest operation as a readable signal.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="load">The factory, which receives a token canceled on reload, cancellation or disposal.</param>
    /// <returns>The owned asynchronous signal, captured by an active SignalScope.</returns>
    public static AsyncSignal<T> FromAsync<T>(Func<CancellationToken, Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        var result = new AsyncSignal<T>(load);
        lock (SignalContext.Gate)
        {
            SignalContext.Current.Scope?.Add(result);
        }

        return result;
    }

    /// <summary>Starts an asynchronous factory that does not accept cancellation.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="load">The asynchronous factory.</param>
    /// <returns>The owned asynchronous signal; obsolete results are ignored even if work cannot be canceled.</returns>
    public static AsyncSignal<T> FromAsync<T>(Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return FromAsync(_ => load());
    }
}
