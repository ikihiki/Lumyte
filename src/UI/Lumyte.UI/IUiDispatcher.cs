namespace Lumyte.UI;

/// <summary>Queues synchronous UI work at an owner update boundary.</summary>
public interface IUiDispatcher
{
    /// <summary>Tests whether the caller runs on the current owner thread.</summary>
    /// <returns>Whether the caller is the owner.</returns>
    bool CheckAccess();

    /// <summary>Queues one action, including when called by the owner.</summary>
    /// <param name="action">Synchronous work.</param>
    /// <param name="cancellationToken">Cancels before execution starts.</param>
    /// <returns>The action completion.</returns>
    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);

    /// <summary>Queues one synchronous query.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">Synchronous work.</param>
    /// <param name="cancellationToken">Cancels before execution starts.</param>
    /// <returns>The query result.</returns>
    ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
}
