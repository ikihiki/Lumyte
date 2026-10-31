namespace Lumyte.Signals;

/// <summary>An immutable snapshot of an asynchronous operation.</summary>
/// <typeparam name="T">The operation's result type.</typeparam>
/// <param name="Status">The operation status.</param>
/// <param name="Result">The result, meaningful when HasValue is true.</param>
/// <param name="Error">The operation error, present only for Failed.</param>
public sealed record AsyncSignalState<T>(AsyncSignalStatus Status, T? Result = default, Exception? Error = null)
{
    /// <summary>Gets a value indicating whether the operation is running.</summary>
    public bool IsLoading => Status == AsyncSignalStatus.Pending;

    /// <summary>Gets a value indicating whether a successful result is available, including null or default.</summary>
    public bool HasValue => Status == AsyncSignalStatus.Succeeded;
}
