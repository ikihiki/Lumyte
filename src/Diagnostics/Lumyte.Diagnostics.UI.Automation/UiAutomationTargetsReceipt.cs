namespace Lumyte.Diagnostics.UI.Automation;

/// <summary>A detached scalar operation receipt.</summary>
/// <param name="Json">The Json value.</param>
public sealed record UiAutomationTargetsReceipt([property: DiagnosticMember(MaxLength = 32768)] string Json);
