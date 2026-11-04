namespace Lumyte.UI.Navigation;

/// <summary>Defines the IUiNavigator lifecycle boundary; operations begin on the UI owner.</summary>
public interface IUiNavigator
{
    /// <summary>Captures and queues a destination on the owner; competing requests settle as Busy.</summary>
    /// <typeparam name="TArgs">The exact registered argument type.</typeparam>
    /// <param name="key">The key value.</param>
    /// <param name="arguments">The arguments value.</param>
    /// <param name="mode">The mode value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    UiNavigationRequest RequestNavigate<TArgs>(UiPageKey<TArgs> key, TArgs arguments, UiNavigationMode mode = UiNavigationMode.Push, CancellationToken cancellationToken = default);

    /// <summary>Captures and queues a destination on the owner; competing requests settle as Busy.</summary>
    /// <param name="route">The route value.</param>
    /// <param name="mode">The mode value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    UiNavigationRequest RequestNavigate(string route, UiNavigationMode mode = UiNavigationMode.Push, CancellationToken cancellationToken = default);

    /// <summary>Queues the previous history destination without retaining its page.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    UiNavigationRequest RequestBack(CancellationToken cancellationToken = default);

    /// <summary>Queues the next history destination without retaining its page.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    UiNavigationRequest RequestForward(CancellationToken cancellationToken = default);

    /// <summary>Copies owner state and history.</summary>
    /// <returns>The operation result or completion.</returns>
    UiNavigationSnapshot CaptureSnapshot();

    /// <summary>Retries retained cleanup; cancellation only cancels the caller wait.</summary>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    /// <returns>The operation result or completion.</returns>
    ValueTask RetryRetirementAsync(CancellationToken cancellationToken = default);
}
