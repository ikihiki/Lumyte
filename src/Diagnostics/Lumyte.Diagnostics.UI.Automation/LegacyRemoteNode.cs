using Lumyte.Diagnostics.UI.Automation.Contracts;

namespace Lumyte.Diagnostics.UI.Automation;

// Keep the C2 node wire shape independent of additions to UiRemoteNode.
internal sealed record LegacyRemoteNode(string Id, string? ParentId, string? TestId, string? Name, string Role, UiRemoteRect Bounds, UiRemoteRect ClippedBounds, bool IsVisible, bool IsEnabled, bool HitTestVisible, bool IsFocusable, bool IsFocused, bool IsPressed, bool HasActivePress, string[] Actions, bool IsTransitioning = false, double? Value = null, bool IsIndeterminate = false);
