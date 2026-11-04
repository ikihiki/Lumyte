using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Copies committed metadata for one stable window context.</summary>
/// <param name="WindowContextId">The window instance identity.</param>
/// <param name="Window">The logical window identity.</param>
/// <param name="Revision">The publication revision.</param>
/// <param name="Current">The committed view, or null before publication and after stop.</param>
public sealed record UiNavigationWindowSnapshot(Guid WindowContextId, UiWindowId Window, ulong Revision, UiNavigationCommittedView? Current);
