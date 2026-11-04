namespace Lumyte.UI.Navigation;

/// <summary>Identifies one atomic publication without exposing argument bodies.</summary>
/// <param name="NavigatorId">The NavigatorId value.</param>
/// <param name="RequestId">The RequestId value.</param>
/// <param name="EntryId">The EntryId value.</param>
/// <param name="ActivationId">The ActivationId value.</param>
/// <param name="Route">The Route value.</param>
public sealed record UiNavigationPublication(Guid NavigatorId, Guid RequestId, Guid EntryId, Guid ActivationId, string Route);
