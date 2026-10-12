using Lumyte.Diagnostics;

namespace Lumyte.Input.Diagnostics;

/// <summary>A detached JSON snapshot consumed by the diagnostic Input page.</summary>
/// <param name="Json">The bounded snapshot document.</param>
public sealed record InputSnapshotReceipt([property: DiagnosticMember(MaxLength = 1048576)] string Json);
