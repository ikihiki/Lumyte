namespace Lumyte.UI.Navigation;

/// <summary>Copies history and local navigation state without exposing argument ownership.</summary>
/// <param name="NavigatorId">The NavigatorId value.</param>
/// <param name="Phase">The Phase value.</param>
/// <param name="Entries">The Entries value.</param>
/// <param name="CurrentIndex">The CurrentIndex value.</param>
/// <param name="ActivationId">The ActivationId value.</param>
/// <param name="CanGoBack">The CanGoBack value.</param>
/// <param name="CanGoForward">The CanGoForward value.</param>
/// <param name="LastResult">The LastResult value.</param>
/// <param name="RetirementFailure">The RetirementFailure value.</param>
public sealed record UiNavigationSnapshot(Guid NavigatorId, UiNavigationPhase Phase, IReadOnlyList<UiNavigationEntry> Entries, int CurrentIndex, Guid? ActivationId, bool CanGoBack, bool CanGoForward, UiNavigationResult? LastResult, Exception? RetirementFailure);
