using Lumyte.Diagnostics;

namespace Lumyte.UI.Application.Navigation.Diagnostics;

/// <summary>Returns a closed schema of published identities, without argument values.</summary>
/// <param name="Json">Schema version 1 JSON, never truncated.</param>
public sealed record UiNavigationStateReceipt([property: DiagnosticMember(MaxLength = 32768)] string Json);
