namespace Lumyte.UI.Navigation;

/// <summary>Reports a settled request; asynchronous errors remain observable as data.</summary>
/// <param name="RequestId">The RequestId value.</param>
/// <param name="Status">The Status value.</param>
/// <param name="Code">The Code value.</param>
/// <param name="EntryId">The EntryId value.</param>
/// <param name="ActivationId">The ActivationId value.</param>
/// <param name="Error">The Error value.</param>
public sealed record UiNavigationResult(Guid RequestId, UiNavigationStatus Status, string Code, Guid? EntryId, Guid? ActivationId, Exception? Error = null);
